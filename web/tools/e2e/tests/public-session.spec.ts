import { expect, test } from '@playwright/test';
import { ALICE, signIn } from './support';

test('public logout reports connection failure and allows a successful retry', async ({ page }) => {
  await signIn(page, ALICE);
  const email = `logout-${Date.now()}@example.test`;
  const api = `http://localhost:${process.env.E2E_API_PORT ?? '5293'}`;
  const issued = await page.request.post(`${api}/contact-links`, { data: { email } });
  expect(issued.ok()).toBe(true);
  let link = '';
  await expect.poll(async () => {
    const response = await page.request.get(`${api}/dev/mail`);
    const messages = await response.json() as { to: string; textBody: string }[];
    link = messages.find((message) => message.to === email)?.textBody.match(/https?:\/\/\S+\/contact\/redeem\?token=\S+/)?.[0] ?? '';
    return link;
  }).not.toBe('');
  const target = new URL(link);
  const publicBase = new URL(process.env.E2E_PUBLIC ?? 'http://acme-dev.localhost:5174');
  target.host = publicBase.host;
  // The contact is a separate visitor, not the owner's localhost cookie jar.
  await page.context().clearCookies();
  await page.goto(target.toString());
  await expect(page.getByText(email, { exact: true })).toBeVisible();
  const sessionBefore = (await page.context().cookies(publicBase.origin)).find((cookie) => cookie.name === 'locintel_session');
  expect(sessionBefore).toBeDefined();

  // Fail the browser-to-SSR hop. Upstream HTTP/network failures are unit-tested.
  await page.route('**/*', (route) => route.request().method() === 'POST' ? route.abort('failed') : route.continue());
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('could not confirm sign-out');
  await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeEnabled();
  expect((await page.context().cookies(publicBase.origin)).find((cookie) => cookie.name === 'locintel_session')?.value).toBe(sessionBefore?.value);

  await page.unroute('**/*');
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByText(email, { exact: true })).toHaveCount(0);
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect((await page.context().cookies(publicBase.origin)).some((cookie) => cookie.name === 'locintel_session')).toBe(false);
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Our locations' })).toBeVisible();
  await expect(page.getByText(email, { exact: true })).toHaveCount(0);
});
