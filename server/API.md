# Leaderboard API

Base URL: the fly.io app, for example `https://halloween2026-board.fly.dev`. JSON responses, UTF-8, always small.
**WebGL builds can only call HTTPS endpoints that answer CORS requests** (the browser blocks everything else), so use the
`https://` URL, and the page must be served from an origin listed in the server's `ALLOWED_ORIGINS`
(for example `https://nerd-omang.github.io`). `UnityWebRequest` in WebGL goes through the browser's fetch, so the same rules apply, and a request from the Editor or a desktop build has no origin and is accepted.

Use `Content-Type: text/plain` for the POST: it is a "simple" CORS request, so the browser sends no preflight request.
(`application/json` also works but costs an extra OPTIONS round trip.)

## Endpoints

### `POST /api/result`

Body: the result text from `LookCode.Encode`, as plain text, or `{ "r": "<text>" }` as JSON. At most 256 bytes
(`MAX_BODY_BYTES`); a real text is 32 characters for a full game.

```
POST /api/result
Content-Type: text/plain

EAqgCLAAAABgYAABQDJAAAAAB4A
```

Response `200`: `{ "rank": 3, "top": true }`

- `top`: the game was stored on the board (it had initials, was a finished game and reached the top N).
- `rank`: 1-based place the score takes (or would take) on the board, `null` when it does not reach the top N.
  Equal scores rank after earlier ones. A game without initials can get a `rank` and `top: false` ("you would be #3, add initials").
  Not meant as an exact global rank beyond the board.
- Every accepted game counts in the statistics, even without initials, even when it does not reach the board.
  Only games with three looks can enter the board (a game ends at the third lost heart); results with fewer looks still count for stats.

Unity sketch:

```csharp
var req = new UnityWebRequest(baseUrl + "/api/result", "POST");
req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.ASCII.GetBytes(text));
req.downloadHandler = new DownloadHandlerBuffer();
req.SetRequestHeader("Content-Type", "text/plain");
yield return req.SendWebRequest();   // req.responseCode, req.downloadHandler.text
```

### `GET /api/top`

`[ { "initials": "BBB", "score": 70, "r": "EAqgCLAAAABgYAABQDJAAAAAB4A" }, ... ]` best first, at most `BOARD_SIZE` (10) entries, `[]` when empty.
`r` is the result text of that game (the same text format as the POST body, canonical: decoded and encoded again by the server),
so the client can decode the three looks that cost the hearts with `LookCode.TryDecode`. Entries stored before `r` existed
have no `r` field: show them without looks.
Cached on the server for `CACHE_TTL_SEC` (20 s) and sent with `Cache-Control: public, max-age=20`. After your own
board entry the server refreshes this list at once, but a browser may still hold the 20 s old copy: show the entry from
the POST answer, or request `/api/top?t=<anything>` (the server ignores the query, the browser treats it as a new URL).

### `GET /api/stats`

```json
{ "games": 1234,
  "items": { "0": 310, "1": 95, "6": 41 },
  "looks": { "16": 77, "4109": 12 } }
```

- `games`: total accepted games. This is the denominator: the text "N games, fooled M times" is
  `games` and `items[bit]` (`0` when the bit is missing).
- `items`: per item bit (see `ItemTable.cs`, item bit `b` is code bit `3 + b`), how many mistakes carried that item.
  If one game has two mistakes with the item, it counts twice.
- `looks`: per whole look, the 32-bit look code in decimal (`LookCode.Pack`), how many mistakes it caused. Limited to the
  `STATS_LOOKS_LIMIT` (100) most fooling looks; JSON key order carries no meaning, sort on the client.
- Cached like `/api/top` (stats may lag by the TTL and are not refreshed per game, which keeps Redis reads rare).

### `GET /healthz`

`{ "ok": true }`, no Redis access, never cached. For the fly health check.

## Wire format (result text)

Unpadded base64url, bits MSB first, zero padding to a whole byte. Three looks make exactly 192 bits = 24 bytes = 32 characters (fewer looks: 75 + 39 per look bits):

| bits | field |
|---|---|
| 4 | version, must be `2` |
| 29 | score, 0..536870911 |
| 1 | has initials |
| 15 | initials, 3 x 5 bits, `A`=0 .. `Z`=25 (ignored when the flag is 0) |
| 2 | look count 0..3 |
| 39 each | look: extended Hamming (39,32) word of the 32-bit look code |
| 16 | checksum: CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF, no reflection, no final xor) over the whole message with these 16 bits set to 0 and the looks as repaired by the Hamming code |
| 8 | reserved, must be 0 (a later version may use them) |

Look code: bit 0 kind (0 Kunmungchi, 1 Omangi), bits 1-2 fur level (0 unless the fur change item is worn), bits 3..31 item bits
(item bit `b` = code bit `3 + b`; 13 items defined now, bits 13..28 reserved). The word has bit 0 = overall parity, bits 1..38 = Hamming positions, data bits go to the non-power-of-two positions in order.
One flipped bit per look is repaired, two are rejected. The action of a mistake is implied by the kind (Kunmungchi sent away,
Omangi given candy). The server rejects looks the game can never produce (unknown item bits, fur level without the fur change, anything `ItemTable.IsValid` refuses) and non-canonical
text (extra bytes, non-zero padding bits, reserved bits set, bad checksum). A flipped bit in a look is repaired; a flipped bit anywhere else fails the checksum. Scores above the 29-bit maximum are clamped by the client, never wrapped. Reference vectors made by the C# code: `server/test/vectors.json`.

## Redis keys

`h26:games` (string), `h26:items` (hash bit -> count), `h26:looks` (hash code -> count), `h26:board` (zset, member `INITIALS:seq`,
score `points * 2^24 + (2^24 - 1 - seq)`), `h26:seq` (string), `h26:boardr` (hash board member -> result text). Details in [README.md](README.md).

## Errors

Body `{ "error": "<code>", "detail": "<optional short text>" }`. CORS headers are present on errors for allowed origins, so the client can read them.

| status | `error` | meaning |
|---|---|---|
| 400 | `invalid_result` | text does not decode (`detail`: `not base64url`, `too short`, `unknown version`, `bad initials`, `bad look count`, `look N damaged`, `look N impossible`, `reserved bits set`, `bad length`, `bad padding`, `bad checksum`) |
| 400 | `bad_request` | JSON body without a string `r`, or unparsable JSON |
| 403 | `origin_not_allowed` | `Origin` header not in `ALLOWED_ORIGINS` |
| 404 | `not_found` | |
| 405 | `method_not_allowed` | `detail` = the allowed method |
| 413 | `payload_too_large` | body over `MAX_BODY_BYTES` |
| 415 | `unsupported_media_type` | only `text/plain` and `application/json` |
| 422 | `implausible_score` | score above `MAX_SCORE` |
| 429 | `rate_limited` | per-IP limit; `Retry-After` in seconds |
| 503 | `unavailable` | Redis unreachable or internal error: retry later, do not retry in a tight loop |

## Limits

Per IP and 60 s window: 5 `POST`, 60 `GET` (`RATE_WRITE_MAX`, `RATE_READ_MAX`, `RATE_WINDOW_SEC`). Body 256 bytes. Score 0..`MAX_SCORE`.
The server does not filter initials. A stopped machine needs about a second to start on the first request: use a timeout of at least 10 s and show the game-over screen without waiting for the answer.
