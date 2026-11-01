# Halloween 2026 leaderboard server

Top-10 board and "fooled by" statistics for the game. The client sends one short text per finished game
(score, optional initials, the up to three looks that cost a heart). API and wire format: [API.md](API.md).

## Language choice

Node.js 22, plain JavaScript (ES modules), `node:http`, no framework. One runtime dependency (`ioredis`).
Go would have a smaller memory footprint, but Go is not installed here and could not be tested; Node starts in
about 150 ms, fits easily in a 256 MB VM, and the Lua/Redis logic and the decoder are testable with the tools at
hand (`ioredis-mock` as a dev dependency runs the real Lua script in tests). No build step, so the Dockerfile is five lines.

## Layout

```
server/
  src/server.js     production entry (REDIS_URL required)
  src/dev.js        same server on an in-memory Redis stand-in: npm run dev
  src/app.js        HTTP routes, CORS, rate limit, body limit, in-memory cache
  src/store.js      Redis keys and the one Lua script per game
  src/lookcode.js   decoder (Hamming 39,32 repair, validation) ported from LookCode.cs
  src/items.js      ItemTable.cs mirror (bits are permanent)
  src/config.js     every environment variable
  test/             unit + end-to-end tests, vectors.json made by the C# code
  fly.toml  Dockerfile  .dockerignore
```

## Redis data model

Prefix `h26:` (`KEY_PREFIX`). One `EVALSHA` per received game does everything below atomically (one command on
per-command billing).

| key | type | content |
|---|---|---|
| `h26:games` | string | `INCR` per accepted game (the denominator of every percentage) |
| `h26:items` | hash | field = item bit number, `HINCRBY field n`: how often a look carrying that item fooled a player |
| `h26:looks` | hash | field = 32-bit look code in decimal, `HINCRBY field 1`: how often that exact look fooled a player |
| `h26:board` | zset | member `INITIALS:seq`, score `points * 2^21 + (2^21 - 1 - seq)` |
| `h26:seq` | string | `INCR` to make members unique |
| `h26:boardr` | hash | field = board member, value = canonical result text (32 characters) of that game; `HSET` on entry, `HDEL` when the member is trimmed |

Commands inside the script, in order: `INCR h26:games`; per look `HINCRBY h26:looks <code> 1`; per item bit
`HINCRBY h26:items <bit> <count>`; then, only for a finished game (`BOARD_MIN_LOOKS`, `BOARD_MIN_SCORE`):
`ZCOUNT h26:board <points*2^21> +inf` (entries that already rank above) and, when that is fewer than N:
`INCR h26:seq`, `ZADD h26:board <score> INITIALS:seq` and `HSET h26:boardr INITIALS:seq <text>` (only when the game has initials),
`ZRANGE h26:board 0 -(N+1)` + `HDEL h26:boardr <those members>`, then `ZREMRANGEBYRANK h26:board 0 -(N+1)` (trim to N). The tie-break part of the score makes an earlier game win a tie
and keeps members unique even for identical initials and score. Reads: `ZREVRANGE h26:board 0 N-1 WITHSCORES` and `HMGET h26:boardr <members>`;
`GET h26:games`, `HGETALL h26:items`, `HGETALL h26:looks`. Everything is bounded: the board holds N members (`boardr` too), `items`
has at most 29 fields, `looks` at most 5760 (the number of producible looks).

Plausibility: the score is rejected above `MAX_SCORE` (default 536870911, the 29-bit limit of the wire format; **set it to what a human
can really reach** once the difficulty curve is known: a bound like `seconds per serve x longest realistic session`),
a look the game cannot produce is rejected, and only games with three looks (the game ends at the third lost heart)
may enter the board; shorter results still count for stats. Anyone who reads the client can still forge a valid
result: this protects against casual spam, not a determined cheater.

## Run and test locally

```bash
cd server
npm install
npm test            # 29 tests, no Redis needed
npm run dev         # http://localhost:8080 on an in-memory Redis
```

Optional run of the end-to-end test against a real Redis (it only writes keys with a random test prefix):

```bash
TEST_REDIS_URL=redis://localhost:6379 npm test
```

Regenerate `test/vectors.json` from the C# code after a client change (item table, rules, codec):

```bash
dotnet run -c Release --project test/vector-gen/VectorGen.csproj -- test/vectors.json
```

The tests also compare the JS item table and the set of all producible looks with the C# ones, so an item added
in `ItemTable.cs` fails the server tests until `src/items.js` and the vectors are updated.
The Redis Lua script was tested here on `ioredis-mock` (Lua via fengari), not on a real Redis: run the
`TEST_REDIS_URL` command above against your Upstash/Redis once before launch.

## Configuration (environment variables)

| variable | default | meaning |
|---|---|---|
| `REDIS_URL` | required | `redis://default:PASSWORD@host:6379` (`rediss://` for TLS). **Secret** |
| `ALLOWED_ORIGINS` | localhost:8000/8080 | comma separated exact origins, e.g. `https://nerd-omang.github.io` (no path, no trailing slash) |
| `PORT` | 8080 | |
| `BOARD_SIZE` | 10 | N of the top N (20 works the same) |
| `MAX_SCORE` | 536870911 | plausibility cap (29 bits at most) |
| `BOARD_MIN_SCORE` / `BOARD_MIN_LOOKS` | 1 / 3 | what may enter the board |
| `MAX_BODY_BYTES` | 256 | request size limit |
| `RATE_WRITE_MAX` / `RATE_READ_MAX` / `RATE_WINDOW_SEC` | 5 / 60 / 60 | per IP per window |
| `CACHE_TTL_SEC` | 20 | in-memory cache and `Cache-Control: max-age` of top and stats |
| `STATS_LOOKS_LIMIT` | 100 | whole looks listed in `/api/stats` |
| `CLIENT_IP_HEADER` | `fly-client-ip` | header carrying the client address; empty = socket address |
| `KEY_PREFIX` | `h26:` | |

Behind fly's proxy the real client address is in `Fly-Client-IP`, which fly sets itself. Do not expose the app by
any other route, because that header could then be forged. HTTPS redirection is done by fly (`force_https`),
the app does not redirect. Run **one** machine: the rate limiter and the cache live in process memory.

## Deploy to fly.io (you run these; nothing here logs in or deploys)

Data decision (read once): all state lives in Redis. The container has no volume and no files to seed, so a deploy
never conflicts with stored data and rolling back the image never touches the board.

1. Log in (once per machine) and check the account:

```bash
fly auth login
fly auth whoami
```

2. Create the app (the name must be globally unique; then edit `app` in `server/fly.toml` to the same name):

```bash
cd server
fly apps create halloween2026-board
```

3. Create Redis. Recommended: Upstash through fly (managed, pay as you go, scale-to-zero friendly). Choose the same
region as the app (`nrt`), no replicas, eviction off. Run `fly redis create --help` to see the flags of your CLI version.

```bash
fly redis create
fly redis status <redis-name>
```

`fly redis status` prints the private URL (`redis://default:...@fly-<name>.upstash.io:6379`).

4. Set the secrets. Keep the URL in your password manager too, nobody can read it back from fly easily
(`fly secrets list` shows names only):

```bash
fly secrets set REDIS_URL="redis://default:PASSWORD@fly-NAME.upstash.io:6379"
```

   If the game page is not served from `https://nerd-omang.github.io`, edit `ALLOWED_ORIGINS` in `fly.toml`
   (or `fly secrets set ALLOWED_ORIGINS=...`). A GitHub Pages project site is still just the origin
   `https://<user>.github.io`, without `/repo-name`.

5. Deploy a single machine and check it:

```bash
fly deploy --ha=false
fly status
fly logs
```

6. Verify with curl (replace the host). The text is a real game: score 42, initials ABC, three looks.

```bash
curl -s https://halloween2026-board.fly.dev/healthz
curl -s -X POST https://halloween2026-board.fly.dev/api/result -H "Content-Type: text/plain" -d "EAqgCLAAAABgYAABQDJAAAAAB4A"
curl -s https://halloween2026-board.fly.dev/api/top
curl -s https://halloween2026-board.fly.dev/api/stats
curl -s -i -H "Origin: https://evil.example" https://halloween2026-board.fly.dev/api/top   # 403
```

   Delete your test entry afterwards with `redis-cli`/Upstash console (`ZREMRANGEBYRANK h26:board 0 -1`, `DEL h26:games h26:items h26:looks h26:seq h26:boardr`)
   or by checking the numbers before the public launch.

7. Optional, for CI or for a session to read status: `fly tokens create deploy -a halloween2026-board` gives a
   narrow token for the `FLY_API_TOKEN` environment variable. Never put tokens or the Redis URL in the repo.

Update later: `cd server && fly deploy --ha=false`. Branch policy of the game repo is not tied to this: the server
is deployed by hand from whatever branch you check out; there is no GitHub workflow for it.

## Cost drivers

Figures are from memory of fly.io and Upstash pricing: check fly.io/docs/about/pricing and upstash.com/pricing before launch.

- Fly machine `shared-cpu-1x` 256 MB: about 2 USD/month if it ran all month. With `auto_stop_machines` and
  `min_machines_running = 0` it is billed only while running, so a seasonal game costs close to nothing, plus a few cents for the stopped root filesystem.
  A request to a stopped machine waits about a second for the start.
- No dedicated IPv4 (about 2 USD/month): do not run `fly ips allocate-v4`; the shared address is free.
- Redis (the real driver):
  - Upstash via fly, pay as you go: a free allowance exists, beyond it roughly 0.2 USD per 100k commands. Per game this server
    issues one script call (counted as one command or as the commands inside it, depending on how Upstash counts scripts: about 5 to 15 in the worst case). Reads are limited by the
    20 s cache: at most 4 commands per refresh while someone is looking. 10k games a month is then well inside cents.
  - Alternative: your own Redis machine on fly (`redis:7-alpine`, `shared-cpu-1x`, 256 MB, 1 GB volume with `--appendonly yes`):
    about 2 to 3 USD/month fixed, unlimited commands, but you operate it, and it must be always on (or at least stopped with its volume) to keep data. Pick this only if command volume would exceed the Upstash price.
  - A plain Upstash account (outside fly) with a free tier works the same way: use its `rediss://` URL.
- Bandwidth: responses are under 1 KB (`/api/stats` up to about 3 KB); negligible.

## Notes for local development hygiene

`node_modules/` and `.env` are git-ignored; tests never touch files (they use the in-memory Redis); the port comes from
`PORT` (`PORT=18080 npm run dev` if 8080 is busy).
