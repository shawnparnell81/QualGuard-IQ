import { defineConfig } from '@playwright/test';

const web = process.env.E2E_WEB_URL ?? 'http://127.0.0.1:5174';
const api = process.env.E2E_API_URL ?? 'http://127.0.0.1:5081';

export default defineConfig({
  testDir: './e2e',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  globalSetup: './e2e/global-setup.ts',
  use: {
    baseURL: web,
    viewport: { width: 1440, height: 900 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    launchOptions: process.env.E2E_CHROME ? { executablePath: process.env.E2E_CHROME } : {},
  },
  webServer: [
    { command: './e2e/serve-api.sh', url: `${api}/health/ready`, timeout: 240_000, reuseExistingServer: false, env: { E2E_API_URL: api } },
    { command: `npx vite --port ${new URL(web).port} --strictPort --host 127.0.0.1`, url: web, timeout: 60_000, reuseExistingServer: false, env: { QGIQ_API_URL: api } },
  ],
});
