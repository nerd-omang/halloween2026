// Local development: same server on an in-memory Redis stand-in (devDependency ioredis-mock, data is lost on exit).
import RedisMock from 'ioredis-mock';
import { loadConfig } from './config.js';
import { createStore } from './store.js';
import { createApp } from './app.js';

const config = loadConfig();
const server = createApp({ config, store: createStore(new RedisMock(), config) });
server.listen(config.port, () => console.log(`dev leaderboard (in-memory) on http://localhost:${config.port}`));
