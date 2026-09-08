import { expect, test } from '@playwright/test';
import { ALICE, signIn } from './support';

test('checklist picker reaches site 201 and recovers each read failure', async ({ page }) => {
  await signIn(page, ALICE);
  // Controlled read responses exercise the UI independently of plan/site quotas.
  // Authentication is real; this test does not claim backend isolation coverage.
  const sites = Array.from({ length: 201 }, (_, index) => ({
    id: `00000000-0000-0000-0000-${String(index + 1).padStart(12, '0')}`,
    name: `Site ${String(index + 1).padStart(3, '0')}`,
  }));
  let failSites = true;
  let failToday = true;
  let failTemplates = true;
  const offsets: number[] = [];
  await page.route('**/api/sites?*', async (route) => {
    if (failSites) return route.fulfill({ status: 503, json: { error: 'unavailable' } });
    const query = new URL(route.request().url()).searchParams;
    const offset = Number(query.get('offset') ?? 0);
    const limit = Number(query.get('limit') ?? 50);
    expect(limit).toBeLessThanOrEqual(200);
    offsets.push(offset);
    await route.fulfill({ json: { items: sites.slice(offset, offset + limit), total: sites.length,
      openCount: sites.length, nextOffset: offset + limit < sites.length ? offset + limit : null } });
  });
  await page.route('**/api/checklists/today?*', (route) => {
    const siteId = new URL(route.request().url()).searchParams.get('siteId');
    return route.fulfill(failToday ? { status: 503, json: { error: 'unavailable' } } : {
      json: { site: sites.find((site) => site.id === siteId)?.name, businessDate: '2026-09-05', lists: [] },
    });
  });
  await page.route('**/api/checklists/templates', (route) => route.fulfill(
    failTemplates ? { status: 503, json: { error: 'unavailable' } } : { json: [] },
  ));
  await page.goto('/checklists');
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load sites.' })).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load templates.' })).toBeVisible();
  failSites = false;
  failTemplates = false;
  await page.getByRole('button', { name: 'Retry sites' }).click();
  await page.getByRole('button', { name: 'Retry templates' }).click();
  await expect(page.getByText('No templates yet.')).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load checklists.' })).toBeVisible();
  failToday = false;
  await page.getByRole('button', { name: 'Retry checklists' }).click();
  await expect(page.getByText('Site 001 ·', { exact: false })).toBeVisible();
  failSites = true;
  await page.getByRole('button', { name: 'Load more sites', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load sites.' })).toBeVisible();
  await expect(page.getByLabel('Checklist site')).toHaveValue(sites[0]!.id);
  failSites = false;
  await page.getByRole('button', { name: 'Retry sites' }).click();
  await expect(page.getByLabel('Checklist site').locator('option')).toHaveCount(100);
  for (let count = 100; count < 201; count += 50) {
    await page.getByRole('button', { name: 'Load more sites', exact: true }).click();
    await expect(page.getByLabel('Checklist site').locator('option')).toHaveCount(Math.min(count + 50, 201));
  }
  await page.getByLabel('Checklist site').selectOption(sites[200]!.id);
  await expect(page.getByText('Site 201 ·', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Load more sites' })).toHaveCount(0);
  expect(offsets).toEqual([0, 50, 100, 150, 200]);
});
