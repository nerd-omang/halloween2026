import http from 'node:http';
import { decode } from './lookcode.js';

class HttpError extends Error {
  constructor(status, code, detail) { super(code); this.status = status; this.code = code; this.detail = detail; }
}

// Fixed-window per-IP counter. Single machine only (see README); entries are swept so the map stays small.
function createLimiter(windowSec, now) {
  const hits = new Map();
  const sweep = setInterval(() => {
    const t = now();
    for (const [k, v] of hits) if (v.reset <= t) hits.delete(k);
  }, windowSec * 1000);
  sweep.unref();
  return {
    // returns 0 when allowed, else the seconds to wait
    hit(key, max) {
      const t = now();
      let e = hits.get(key);
      if (!e || e.reset <= t) { e = { count: 0, reset: t + windowSec * 1000 }; hits.set(key, e); }
      if (hits.size > 50000) hits.clear(); // memory guard against a flood of distinct addresses
      if (++e.count > max) return Math.max(1, Math.ceil((e.reset - t) / 1000));
      return 0;
    },
    stop() { clearInterval(sweep); },
  };
}

function readBody(req, limit) {
  return new Promise((resolve, reject) => {
    const declared = req.headers['content-length'];
    if (declared !== undefined && Number(declared) > limit) return reject(new HttpError(413, 'payload_too_large'));
    const chunks = [];
    let size = 0;
    req.on('data', (c) => {
      size += c.length;
      if (size > limit) { reject(new HttpError(413, 'payload_too_large')); req.destroy(); return; }
      chunks.push(c);
    });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

function createCache(ttlSec, now) {
  const entries = new Map();
  return {
    // loader returns an object; the serialized body is cached and shared by concurrent callers
    async get(name, loader) {
      const e = entries.get(name);
      if (e && (e.promise || e.at + ttlSec * 1000 > now())) return e.promise ? e.promise : e.body;
      const promise = loader().then((v) => JSON.stringify(v));
      entries.set(name, { promise, at: 0, body: null });
      try {
        const body = await promise;
        entries.set(name, { promise: null, at: now(), body });
        return body;
      } catch (err) {
        entries.delete(name);
        throw err;
      }
    },
    drop(name) { entries.delete(name); },
  };
}

export function createApp({ config, store, now = Date.now }) {
  const limiter = createLimiter(config.rateWindowSec, now);
  const cache = createCache(config.cacheTtlSec, now);
  const origins = new Set(config.allowedOrigins);

  function clientIp(req) {
    const h = config.clientIpHeader && req.headers[config.clientIpHeader];
    return (h ? String(h).split(',')[0].trim() : req.socket.remoteAddress) || 'unknown';
  }

  function send(res, status, body, extra = {}) {
    const text = typeof body === 'string' ? body : JSON.stringify(body);
    res.writeHead(status, {
      'Content-Type': 'application/json; charset=utf-8',
      'Content-Length': Buffer.byteLength(text),
      'X-Content-Type-Options': 'nosniff',
      ...extra,
    });
    res.end(text);
  }

  async function handle(req, res) {
    const url = new URL(req.url, 'http://x');
    const path = url.pathname;
    const method = req.method;

    if (path === '/healthz') return send(res, 200, { ok: true }, { 'Cache-Control': 'no-store' });

    const headers = { 'Cache-Control': 'no-store', Vary: 'Origin' };
    const origin = req.headers.origin;
    if (origin !== undefined) {
      if (!origins.has(origin)) throw new HttpError(403, 'origin_not_allowed');
      headers['Access-Control-Allow-Origin'] = origin;
    }
    if (method === 'OPTIONS') {
      return send(res, 204, '', {
        ...headers,
        'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type',
        'Access-Control-Max-Age': '86400',
      });
    }

    const route = { '/api/result': 'POST', '/api/top': 'GET', '/api/stats': 'GET' }[path];
    if (!route) throw new HttpError(404, 'not_found');
    if (method !== route && !(route === 'GET' && method === 'HEAD')) {
      throw new HttpError(405, 'method_not_allowed', route);
    }

    const wait = limiter.hit(`${route}|${clientIp(req)}`, route === 'POST' ? config.rateWriteMax : config.rateReadMax);
    if (wait) { const e = new HttpError(429, 'rate_limited'); e.retryAfter = wait; throw e; }

    if (route === 'GET') {
      const body = await cache.get(path, path === '/api/top' ? () => store.top() : () => store.stats());
      return send(res, 200, method === 'HEAD' ? '' : body, {
        ...headers,
        'Cache-Control': `public, max-age=${config.cacheTtlSec}`,
      });
    }

    const type = (req.headers['content-type'] || 'text/plain').split(';')[0].trim().toLowerCase();
    if (type !== 'text/plain' && type !== 'application/json') throw new HttpError(415, 'unsupported_media_type');
    let text = (await readBody(req, config.maxBodyBytes)).trim();
    if (type === 'application/json' || text.startsWith('{')) {
      let parsed;
      try { parsed = JSON.parse(text); } catch { throw new HttpError(400, 'bad_request', 'json'); }
      if (!parsed || typeof parsed.r !== 'string') throw new HttpError(400, 'bad_request', 'missing r');
      text = parsed.r.trim();
    }

    const result = decode(text);
    if (!result.ok) throw new HttpError(400, 'invalid_result', result.error);
    if (result.score > config.maxScore) throw new HttpError(422, 'implausible_score');

    const outcome = await store.submit(result);
    if (outcome.top) cache.drop('/api/top'); // stats may lag by the cache TTL; that is what keeps Redis reads rare
    return send(res, 200, outcome, headers);
  }

  const server = http.createServer((req, res) => {
    handle(req, res).catch((err) => {
      if (res.headersSent) return;
      const known = err instanceof HttpError;
      if (!known) console.error('request failed:', err);
      const origin = req.headers.origin;
      const extra = { 'Cache-Control': 'no-store', Vary: 'Origin' };
      if (origin !== undefined && origins.has(origin)) extra['Access-Control-Allow-Origin'] = origin;
      if (err.status === 429 && err.retryAfter) extra['Retry-After'] = String(err.retryAfter);
      send(res, known ? err.status : 503, known ? { error: err.code, ...(err.detail ? { detail: err.detail } : {}) } : { error: 'unavailable' }, extra);
    });
  });
  // slowloris guard and small sockets for a tiny API
  server.headersTimeout = 10000;
  server.requestTimeout = 10000;
  server.keepAliveTimeout = 5000;
  server.on('close', () => limiter.stop());
  return server;
}
