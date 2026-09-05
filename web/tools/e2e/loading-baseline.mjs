// Run through E2E_LOADING_BASELINE=1 tools/e2e-stack.sh --project=chromium.
// Fresh browser contexts, built assets, local network, no CPU/network throttling.
import assert from 'node:assert/strict';
import { chromium } from '@playwright/test';

const baseURL = process.env.E2E_CONSOLE ?? 'http://localhost:5173';
const publicURL = process.env.E2E_PUBLIC ?? 'http://acme-dev.localhost:5174';
const browser = await chromium.launch();
try {
  const login = await browser.newContext({ baseURL });
  const page = await login.newPage();
  await page.goto('/auth/login?hint=alice%40acme.test');
  await page.getByRole('navigation').first().getByRole('link', { name: 'Dashboard' }).waitFor();
  const storageState = await login.storageState();
  await login.close();
  for (const [name, url, heading] of [
    ['console sites', `${baseURL}/sites`, 'Sites'],
    ['public SSR locator', publicURL, 'Our locations'],
  ]) {
    const samples = [];
    for (let i = 0; i < 5; i++) {
      const context = await browser.newContext({ storageState, baseURL });
      try {
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const start = performance.now();
        const response = await page.goto(url);
        assert(response?.ok(), `${name}: document request failed`);
        await page.getByRole('heading', { name: heading, exact: true }).waitFor();
        const readyMs = performance.now() - start;
        const timings = await page.evaluate(() => {
          const nav = performance.getEntriesByType('navigation')[0];
          const assets = performance.getEntriesByType('resource');
          return {
            ttfbMs: nav.responseStart,
            domContentLoadedMs: nav.domContentLoadedEventEnd,
            transferredBytes: nav.transferSize + assets.reduce((sum, entry) => sum + entry.transferSize, 0),
            resources: assets.length,
          };
        });
        assert.equal(errors.length, 0, errors.join('\n'));
        samples.push({ readyMs, ...timings });
      } finally { await context.close(); }
    }
    console.log(JSON.stringify({ target: name, samples, context: 'five fresh Chromium contexts; seeded dev dataset; visible heading, not full interaction readiness; no throttling' }));
  }
} finally { await browser.close(); }
