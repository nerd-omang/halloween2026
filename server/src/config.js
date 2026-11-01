// All tunables come from environment variables; nothing secret has a default.
export function loadConfig(env = process.env) {
  const int = (name, fallback, min = 0) => {
    const raw = env[name];
    if (raw === undefined || raw === '') return fallback;
    const v = Number(raw);
    if (!Number.isInteger(v) || v < min) throw new Error(`${name} must be an integer >= ${min}`);
    return v;
  };
  return {
    port: int('PORT', 8080, 1),
    redisUrl: env.REDIS_URL || '',
    // Comma separated exact origins, e.g. "https://nerd-omang.github.io,http://localhost:8000".
    allowedOrigins: (env.ALLOWED_ORIGINS || 'http://localhost:8000,http://localhost:8080,http://127.0.0.1:8000')
      .split(',').map((s) => s.trim()).filter(Boolean),
    boardSize: int('BOARD_SIZE', 20, 1),              // N of the top N
    maxScore: Math.min(int('MAX_SCORE', 536870911, 1), 536870911), // plausibility cap, at most the 29 bits of the wire format
    boardMinScore: int('BOARD_MIN_SCORE', 1, 0),       // a score below this never enters the board
    boardMinLooks: int('BOARD_MIN_LOOKS', 3, 0),       // only finished games (3 hearts lost) enter the board
    maxBodyBytes: int('MAX_BODY_BYTES', 256, 16),
    rateWriteMax: int('RATE_WRITE_MAX', 5, 1),         // POST /api/result per IP per window
    rateReadMax: int('RATE_READ_MAX', 60, 1),          // GET per IP per window
    rateWindowSec: int('RATE_WINDOW_SEC', 60, 1),
    cacheTtlSec: int('CACHE_TTL_SEC', 20, 0),          // in-memory and Cache-Control max-age for top/stats
    statsLooksLimit: int('STATS_LOOKS_LIMIT', 100, 1), // whole looks listed in /api/stats (the most fooling ones; JSON key order is not meaningful)
    clientIpHeader: (env.CLIENT_IP_HEADER === undefined ? 'fly-client-ip' : env.CLIENT_IP_HEADER).toLowerCase(),
    keyPrefix: env.KEY_PREFIX || 'h26:',
  };
}
