import { defineConfig, devices } from '@playwright/test';

// The stack (Postgres, migrate + api roles with the local auth provider, the
// built console and public SSR servers) is booted by tools/e2e-stack.sh, locally and in CI;
// this config only drives the browser. Sign-in is by hint: an automated run
// never types credentials (LOCINTEL_AUTH=local in the AppHost is the same idea).
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  // Projects share seeded accounts and mutate them; avoid cross-browser fixture races.
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_CONSOLE ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
});
