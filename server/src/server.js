import Redis from 'ioredis';
import { loadConfig } from './config.js';
import { createStore } from './store.js';
import { createApp } from './app.js';

const config = loadConfig();
if (!config.redisUrl) {
  console.error('REDIS_URL is not set (use "npm run dev" for an in-memory Redis stand-in)');
  process.exit(1);
}

// Upstash and fly Redis URLs: redis://default:password@host:port, or rediss:// for TLS.
// Fly's private network is IPv6, hence family 0 (try both). Lazy connect keeps a cold start fast and an
// unreachable Redis from crashing the process; requests then answer 503.
const redis = new Redis(config.redisUrl, {
  family: 0,
  lazyConnect: false,
  maxRetriesPerRequest: 2,
  connectTimeout: 5000,
  enableOfflineQueue: true,
});
redis.on('error', (e) => console.error('redis:', e.message));

const server = createApp({ config, store: createStore(redis, config) });
server.listen(config.port, '0.0.0.0', () => console.log(`leaderboard listening on :${config.port}`));

const stop = () => { server.close(() => redis.quit().finally(() => process.exit(0))); setTimeout(() => process.exit(0), 3000).unref(); };
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
