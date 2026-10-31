// End-to-end: real HTTP server, real Lua script. Uses ioredis-mock (Lua through fengari) unless TEST_REDIS_URL
// points at a real Redis (it will FLUSH the keys under the test prefix only).
import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import Redis from 'ioredis';
import RedisMock from 'ioredis-mock';
import { loadConfig } from '../src/config.js';
import { createStore } from '../src/store.js';
import { createApp } from '../src/app.js';
import { encode } from '../src/lookcode.js';

const ORIGIN = 'https://nerd-omang.github.io';
const KUN_HORN = (1 << 1) << 3; // Kunmungchi + horn band (needs no fur)
const OMA_FUR_BAG = (((1 << 0) | (1 << 9)) << 3) | (2 << 1) | 1; // Omangi + fur level 2 + pumpkin basket
const OMA_PLAIN = 1;

async function boot(env = {}, now) {
  const config = loadConfig({
    ALLOWED_ORIGINS: `${ORIGIN},http://localhost:8000`,
    RATE_WRITE_MAX: '1000',
    RATE_READ_MAX: '1000',
    CACHE_TTL_SEC: '0',
    KEY_PREFIX: `t${Math.random().toString(36).slice(2)}:`,
    ...env,
  });
  const redis = process.env.TEST_REDIS_URL ? new Redis(process.env.TEST_REDIS_URL) : new RedisMock();
  const server = createApp({ config, store: createStore(redis, config), now });
  server.listen(0);
  await once(server, 'listening');
  const base = `http://127.0.0.1:${server.address().port}`;
  const call = async (path, init = {}) => {
    const res = await fetch(base + path, init);
    const text = await res.text();
    let json = null;
    try { json = JSON.parse(text); } catch { /* empty body */ }
    return { status: res.status, headers: res.headers, text, json };
  };
  const post = (body, headers = {}) => call('/api/result', { method: 'POST', body, headers: { 'Content-Type': 'text/plain', ...headers } });
  const close = async () => { server.close(); server.closeAllConnections(); redis.disconnect(); };
  return { config, redis, call, post, close };
}

const game = (score, initials, codes = [KUN_HORN, OMA_FUR_BAG, OMA_PLAIN]) => encode(score, initials, codes);

test('board: ranks, ties, trim to N, no-initials entries only count for stats', async (t) => {
  const s = await boot({ BOARD_SIZE: '3' });
  t.after(s.close);

  assert.deepEqual((await s.post(game(50, 'AAA'))).json, { rank: 1, top: true });
  assert.deepEqual((await s.post(game(70, 'BBB'))).json, { rank: 1, top: true });
  assert.deepEqual((await s.post(game(50, 'AAA'))).json, { rank: 3, top: true }, 'same initials and score may repeat, later one ranks below');
  assert.deepEqual((await s.post(game(50, 'CCC'))).json, { rank: null, top: false }, 'board full, tie with the lowest does not enter');
  assert.deepEqual((await s.post(game(60, null))).json, { rank: 2, top: false }, 'no initials: rank shown, never stored');
  assert.deepEqual((await s.post(game(10, null))).json, { rank: null, top: false });
  assert.deepEqual((await s.post(game(60, 'DDD'))).json, { rank: 2, top: true });

  const top = (await s.call('/api/top')).json;
  assert.deepEqual(top, [
    { initials: 'BBB', score: 70 },
    { initials: 'DDD', score: 60 },
    { initials: 'AAA', score: 50 },
  ]);
  assert.equal(await s.redis.zcard(`${s.config.keyPrefix}board`), 3, 'trimmed to N');
});

test('board entry needs a finished game and a score above the minimum', async (t) => {
  const s = await boot();
  t.after(s.close);
  assert.deepEqual((await s.post(game(500, 'AAA', [KUN_HORN]))).json, { rank: null, top: false });
  assert.deepEqual((await s.post(game(0, 'AAA'))).json, { rank: null, top: false });
  assert.deepEqual((await s.call('/api/top')).json, []);
  assert.equal((await s.call('/api/stats')).json.games, 2, 'still counted for stats');
});

test('stats: games, items per bit, whole looks', async (t) => {
  const s = await boot();
  t.after(s.close);
  await s.post(game(10, 'AAA')); // horn, fur+basket, plain Omangi
  await s.post(game(20, null, [KUN_HORN, KUN_HORN]));
  await s.post(game(30, null, []));
  const stats = (await s.call('/api/stats')).json;
  assert.equal(stats.games, 3);
  assert.deepEqual(stats.items, { 0: 1, 1: 3, 9: 1 });
  assert.deepEqual(stats.looks, { [KUN_HORN]: 3, [OMA_FUR_BAG]: 1, [OMA_PLAIN]: 1 });
});

test('stats limit the number of listed looks', async (t) => {
  const s = await boot({ STATS_LOOKS_LIMIT: '1' });
  t.after(s.close);
  await s.post(game(1, null, [KUN_HORN, KUN_HORN, OMA_PLAIN]));
  assert.deepEqual((await s.call('/api/stats')).json.looks, { [KUN_HORN]: 2 });
});

test('request body: plain text, json, size, content type', async (t) => {
  const s = await boot({ MAX_BODY_BYTES: '64' });
  t.after(s.close);
  const text = game(5, 'XYZ');
  assert.equal((await s.post(text)).status, 200);
  assert.equal((await s.post(`${text}\n`)).status, 200);
  assert.equal((await s.post(JSON.stringify({ r: text }), { 'Content-Type': 'application/json' })).status, 200);
  assert.equal((await s.post(JSON.stringify({ r: text }))).status, 200, 'json sniffed even under text/plain');
  assert.equal((await s.post('{"x":1}')).json.error, 'bad_request');
  assert.equal((await s.post('{oops')).json.error, 'bad_request');
  assert.equal((await s.post('x'.repeat(65))).status, 413);
  assert.equal((await s.post(text, { 'Content-Type': 'image/png' })).status, 415);
  const bad = await s.post('AAAA');
  assert.equal(bad.status, 400);
  assert.equal(bad.json.error, 'invalid_result');
  assert.equal((await s.call('/api/result')).status, 405);
  assert.equal((await s.call('/api/nope')).status, 404);
  assert.equal((await s.call('/api/top', { method: 'POST', body: 'x' })).status, 405);
});

test('plausibility cap on the score', async (t) => {
  const s = await boot({ MAX_SCORE: '1000' });
  t.after(s.close);
  assert.equal((await s.post(game(1000, 'AAA'))).status, 200);
  const r = await s.post(game(1001, 'AAA'));
  assert.equal(r.status, 422);
  assert.equal(r.json.error, 'implausible_score');
  assert.equal((await s.call('/api/stats')).json.games, 1, 'rejected games are not counted');
});

test('CORS: allowed origins only, preflight, headers on errors', async (t) => {
  const s = await boot();
  t.after(s.close);
  const ok = await s.call('/api/top', { headers: { Origin: ORIGIN } });
  assert.equal(ok.headers.get('access-control-allow-origin'), ORIGIN);
  const local = await s.call('/api/top', { headers: { Origin: 'http://localhost:8000' } });
  assert.equal(local.headers.get('access-control-allow-origin'), 'http://localhost:8000');
  const evil = await s.call('/api/top', { headers: { Origin: 'https://evil.example' } });
  assert.equal(evil.status, 403);
  assert.equal(evil.headers.get('access-control-allow-origin'), null);
  const pre = await s.call('/api/result', { method: 'OPTIONS', headers: { Origin: ORIGIN, 'Access-Control-Request-Method': 'POST' } });
  assert.equal(pre.status, 204);
  assert.match(pre.headers.get('access-control-allow-methods'), /POST/);
  const err = await s.call('/api/result', { method: 'POST', body: 'AAAA', headers: { Origin: ORIGIN } });
  assert.equal(err.status, 400);
  assert.equal(err.headers.get('access-control-allow-origin'), ORIGIN, 'the browser can read errors too');
  assert.equal((await s.call('/healthz')).status, 200);
});

test('rate limit per IP with Retry-After, window resets', async (t) => {
  let clock = 1_000_000;
  const s = await boot({ RATE_WRITE_MAX: '2', RATE_WINDOW_SEC: '60' }, () => clock);
  t.after(s.close);
  const text = game(5, null);
  const ip = (n) => ({ 'Fly-Client-IP': n });
  assert.equal((await s.post(text, ip('1.1.1.1'))).status, 200);
  assert.equal((await s.post(text, ip('1.1.1.1'))).status, 200);
  const limited = await s.post(text, ip('1.1.1.1'));
  assert.equal(limited.status, 429);
  assert.equal(limited.json.error, 'rate_limited');
  assert.ok(Number(limited.headers.get('retry-after')) > 0);
  assert.equal((await s.post(text, ip('2.2.2.2'))).status, 200, 'other address unaffected');
  clock += 61_000;
  assert.equal((await s.post(text, ip('1.1.1.1'))).status, 200);
});

test('top and stats are cached in memory and sent with Cache-Control', async (t) => {
  let clock = 1_000_000;
  const s = await boot({ CACHE_TTL_SEC: '20' }, () => clock);
  t.after(s.close);
  const first = await s.call('/api/stats');
  assert.equal(first.headers.get('cache-control'), 'public, max-age=20');
  assert.equal(first.json.games, 0);
  await s.post(game(10, 'AAA'));
  assert.equal((await s.call('/api/stats')).json.games, 0, 'served from memory');
  clock += 21_000;
  assert.equal((await s.call('/api/stats')).json.games, 1, 'refreshed after the TTL');
  // a game that enters the board shows up at once, a stale board would hide the player's own entry
  assert.equal((await s.call('/api/top')).json.length, 1);
  await s.post(game(20, 'BBB'));
  assert.equal((await s.call('/api/top')).json.length, 2);
  assert.equal((await s.post(game(1, null))).headers.get('cache-control'), 'no-store');
});

test('concurrent submissions keep the board consistent', async (t) => {
  const s = await boot({ BOARD_SIZE: '5' });
  t.after(s.close);
  await Promise.all(Array.from({ length: 20 }, (_, i) => s.post(game(100 + i, 'AAA'))));
  const top = (await s.call('/api/top')).json;
  assert.deepEqual(top.map((e) => e.score), [119, 118, 117, 116, 115]);
  assert.equal((await s.call('/api/stats')).json.games, 20);
});
