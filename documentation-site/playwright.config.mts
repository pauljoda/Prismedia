import {defineConfig} from '@playwright/test';
import {fileURLToPath} from 'node:url';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  reporter: 'list',
  use: {
    channel: process.env.PLAYWRIGHT_CHANNEL,
    baseURL: 'http://127.0.0.1:3001/Prismedia/',
    viewport: {width: 1280, height: 800},
    contextOptions: {reducedMotion: 'no-preference'},
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'pnpm docs:serve -- --host 127.0.0.1 --port 3001 --no-open',
    cwd: fileURLToPath(new URL('..', import.meta.url)),
    url: 'http://127.0.0.1:3001/Prismedia/',
    reuseExistingServer: !process.env.CI,
  },
});
