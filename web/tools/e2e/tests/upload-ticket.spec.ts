import { expect, test } from '@playwright/test';
import { nav } from './support';

test('upload sends ticket headers and stops on storage failure before retrying', async ({ page }) => {
  // This workflow must not inherit the seeded owner's rate-limit window from
  // the preceding browser tests. Keep the production limits unchanged.
  const stamp = `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
  await page.goto(`/auth/login?hint=upload-${stamp}@example.test`);
  await page.getByLabel('Organization name').fill(`Upload ${stamp}`);
  await page.getByRole('button', { name: 'Create organization' }).click();
  await expect(nav(page).getByRole('link', { name: 'Dashboard' })).toBeVisible();
  await page.goto('/files');
  await page.route('**/api/files', async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    const response = await route.fetch();
    const body = await response.json();
    body.ticket.headers['X-Upload-Probe'] = 'required';
    await route.fulfill({ response, json: body });
  });

  const sentHeaders: (string | undefined)[] = [];
  let rejectUpload = true;
  let completions = 0;
  page.on('request', (request) => {
    if (request.method() === 'POST' && /\/api\/files\/[^/]+\/complete$/.test(request.url()))
      completions++;
  });
  await page.route('**/objects/upload/*', async (route) => {
    sentHeaders.push(route.request().headers()['x-upload-probe']);
    if (rejectUpload) await route.fulfill({ status: 503, body: 'storage unavailable' });
    else await route.continue();
  });

  const file = { name: `upload-retry-${Date.now()}.txt`, mimeType: 'text/plain', buffer: Buffer.from('safe bytes') };
  await page.locator('input[type=file]').setInputFiles(file);
  await expect.poll(() => sentHeaders).toEqual(['required']);
  await expect(page.getByText('Error: Upload failed (HTTP 503). Please try again.', { exact: true })).toBeVisible();
  expect(completions).toBe(0);
  await expect(page.getByRole('button', { name: 'Upload file' })).toBeEnabled();

  rejectUpload = false;
  await page.locator('input[type=file]').setInputFiles({ ...file, name: `retried-${file.name}` });
  await expect(page.getByRole('row').filter({ hasText: `retried-${file.name}` })).toContainText('Clean');
  expect(sentHeaders).toEqual(['required', 'required']);
  expect(completions).toBe(1);
});
