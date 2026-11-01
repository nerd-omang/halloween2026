// Redis access. One Lua script per submitted game keeps it to a single command (cheap on per-command billing)
// and makes the rank/insert/trim sequence atomic.
//
// Keys (prefix = KEY_PREFIX, default "h26:"):
//   {p}games   string  total finished games received (INCR)
//   {p}items   hash    field = item bit number, value = times that item was on a look that fooled a player (HINCRBY)
//   {p}looks   hash    field = 32-bit look code (decimal), value = times that exact look fooled a player (HINCRBY)
//   {p}board   zset    member "INITIALS:seq", score = points * 2^21 + (2^21 - 1 - seq)  (earlier game wins a tie)
//   {p}seq     string  member id counter (INCR)
//   {p}boardr  hash    field = board member, value = canonical result text of that game (HSET; HDEL when trimmed)
//   {p}seen    set     canonical result texts already received (SADD): the same score, initials and looks are taken once.
//                      Filled from the board's texts the first time it is missing, so earlier board games count as seen.
import { encode } from './lookcode.js';

const SUBMIT_LUA = `
local N = tonumber(ARGV[1])
local score = tonumber(ARGV[2])
local initials = ARGV[3]
local eligible = ARGV[4] == '1'
local text = ARGV[5]
local nLooks = tonumber(ARGV[6])
local SH = 2097152.0 -- 2^21; a float on purpose, so it stays exact even where Lua has 32-bit integers (ioredis-mock)

-- the same result again (a retry, or a local record shared twice) changes nothing
if redis.call('EXISTS', KEYS[7]) == 0 then
  for _, v in ipairs(redis.call('HVALS', KEYS[6])) do redis.call('SADD', KEYS[7], v) end
end
if redis.call('SADD', KEYS[7], text) == 0 then return { 0, 0, 1 } end

redis.call('INCR', KEYS[1])
for i = 1, nLooks do
  redis.call('HINCRBY', KEYS[3], ARGV[6 + i], 1)
end
local i = 7 + nLooks
while i < #ARGV do
  redis.call('HINCRBY', KEYS[2], ARGV[i], ARGV[i + 1])
  i = i + 2
end

local rank = 0
local entered = 0
if eligible then
  -- entries already on the board with at least this many points rank above a new one
  local better = redis.call('ZCOUNT', KEYS[4], string.format('%.0f', score * SH), '+inf')
  if better < N then
    rank = better + 1
    if initials ~= '' then
      local seq = redis.call('INCR', KEYS[5])
      local tie = seq
      if tie > SH - 1 then tie = SH - 1 end
      local z = score * SH + (SH - 1 - tie)
      local member = initials .. ':' .. seq
      redis.call('ZADD', KEYS[4], string.format('%.0f', z), member)
      redis.call('HSET', KEYS[6], member, text)
      -- members pushed out of the top N lose their result text too, so the hash never outgrows the board
      local gone = redis.call('ZRANGE', KEYS[4], 0, -(N + 1))
      for _, m in ipairs(gone) do redis.call('HDEL', KEYS[6], m) end
      redis.call('ZREMRANGEBYRANK', KEYS[4], 0, -(N + 1))
      entered = 1
    end
  end
end
return { rank, entered, 0 }
`;

// Points are 29 bits and the tie-break 21 (2 million board entries): 50 bits together, three bits below the 53 a double
// holds exactly, so no Lua or Redis implementation can come near its limit.
const SHIFT = 2 ** 21;

export function createStore(redis, config) {
  const p = config.keyPrefix;
  const keys = {
    games: `${p}games`, items: `${p}items`, looks: `${p}looks`, board: `${p}board`, seq: `${p}seq`, boardr: `${p}boardr`, seen: `${p}seen`,
  };
  redis.defineCommand('h26submit', { numberOfKeys: 7, lua: SUBMIT_LUA });

  return {
    keys,

    // result = decoded game. Returns { rank (1..N or null), top (stored on the board) }, or
    // { rank: null, top: false, duplicate: true } when the same game was received before (nothing is counted).
    async submit(result) {
      const eligible = result.score >= config.boardMinScore && result.looks.length >= config.boardMinLooks;
      const itemCounts = new Map();
      for (const look of result.looks) {
        for (let bit = 0; bit < 29; bit++) {
          if ((look.items >>> bit) & 1) itemCounts.set(bit, (itemCounts.get(bit) || 0) + 1);
        }
      }
      // What is stored is the canonical text: the decoded game encoded again, so a repaired bit never survives.
      const text = encode(result.score, result.initials, result.looks.map((l) => l.code));
      const args = [config.boardSize, result.score, result.initials || '', eligible ? '1' : '0', text, result.looks.length];
      for (const look of result.looks) args.push(look.code);
      for (const [bit, count] of itemCounts) args.push(bit, count);
      const [rank, entered, duplicate] = await redis.h26submit(
        keys.games, keys.items, keys.looks, keys.board, keys.seq, keys.boardr, keys.seen, ...args,
      );
      if (Number(duplicate) === 1) return { rank: null, top: false, duplicate: true };
      return { rank: rank > 0 ? Number(rank) : null, top: Number(entered) === 1 };
    },

    // [{ initials, score, r? }]; r (the result text) is missing for entries stored before it existed.
    async top() {
      const flat = await redis.zrevrange(keys.board, 0, config.boardSize - 1, 'WITHSCORES');
      const members = [];
      const out = [];
      for (let i = 0; i < flat.length; i += 2) {
        members.push(flat[i]);
        out.push({ initials: flat[i].split(':')[0], score: Math.floor(Number(flat[i + 1]) / SHIFT) });
      }
      if (members.length > 0) {
        const texts = await redis.hmget(keys.boardr, ...members);
        texts.forEach((t, i) => { if (t) out[i].r = t; });
      }
      return out;
    },

    async stats() {
      const [games, items, looks] = await Promise.all([
        redis.get(keys.games), redis.hgetall(keys.items), redis.hgetall(keys.looks),
      ]);
      const itemOut = {};
      for (const [bit, n] of Object.entries(items)) itemOut[bit] = Number(n);
      const lookList = Object.entries(looks).map(([code, n]) => [Number(code), Number(n)]);
      lookList.sort((a, b) => b[1] - a[1] || a[0] - b[0]);
      const lookOut = {};
      for (const [code, n] of lookList.slice(0, config.statsLooksLimit)) lookOut[code] = n;
      return { games: Number(games || 0), items: itemOut, looks: lookOut };
    },

    async ping() { return redis.ping(); },
  };
}
