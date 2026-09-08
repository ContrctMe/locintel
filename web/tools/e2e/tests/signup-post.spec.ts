import { expect, test } from '@playwright/test';
import { ALICE, signIn } from './support';

test('signup submits an explicit POST form and follows the authentication redirect', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  const email = page.getByLabel('Email for your new account');
  await expect(page.getByRole('button', { name: 'Create account', exact: true })).toBeDisabled();
  await email.fill(`signup-${Date.now()}@example.test`);
  const submitted = page.waitForRequest((request) => new URL(request.url()).pathname === '/auth/signup');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  const request = await submitted;
  expect(request.method()).toBe('POST');
  expect(new URL(request.url()).search).toBe('');
  expect(request.postData()).toContain('email=');
  await expect(page.getByRole('heading', { name: 'Create your organization' })).toBeVisible();
});

test('evidence download is an explicit POST and records access issuance', async ({ page }) => {
  await signIn(page, ALICE);
  const createdFile = await page.request.post('/api/files', {
    data: { name: 'issuance.txt', contentType: 'text/plain', sizeBytes: 4 },
  });
  expect(createdFile.ok()).toBeTruthy();
  const { fileId, ticket } = await createdFile.json();
  expect((await page.request.put(ticket.url, { data: Buffer.from('test'), headers: ticket.headers })).ok()).toBeTruthy();
  expect((await page.request.post(`/api/files/${fileId}/complete`)).ok()).toBeTruthy();
  await expect.poll(async () => (await (await page.request.get(`/api/files/${fileId}`)).json()).status).toBe('Clean');
  const createdCase = await page.request.post('/api/cases', { data: { title: 'Issuance browser check', priority: 'Medium' } });
  expect(createdCase.ok()).toBeTruthy();
  const { id } = await createdCase.json();
  const evidence = await page.request.post(`/api/cases/${id}/evidence`, { data: { fileId } });
  expect(evidence.ok()).toBeTruthy();
  const { id: evidenceId } = await evidence.json();
  const path = `/api/cases/${id}/evidence/${evidenceId}/download`;
  expect((await page.request.get(path)).status()).toBe(405);
  await page.goto(`/cases/${id}`);
  const issued = page.waitForResponse((response) => new URL(response.url()).pathname === path && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Download', exact: true }).click();
  expect((await issued).ok()).toBeTruthy();
  const custody = await (await page.request.get(`/api/cases/${id}/custody`)).json();
  expect(custody.events.map((event: { action: string }) => event.action)).toContain('DownloadAccessIssued');
});
