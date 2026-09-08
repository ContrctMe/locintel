import { expect, test } from '@playwright/test';
import { ALICE, signIn } from './support';

test('checklist picker finds site 201 by search and recovers each read failure', async ({ page }) => {
  await signIn(page, ALICE);
  // Controlled read responses exercise the UI independently of plan/site quotas.
  // Authentication is real; this test does not claim backend isolation coverage.
  const sites = Array.from({ length: 201 }, (_, index) => ({
    id: `00000000-0000-0000-0000-${String(index + 1).padStart(12, '0')}`,
    name: `Site ${String(index + 1).padStart(3, '0')}`,
    city: null,
  }));
  let failSites = true;
  let failToday = true;
  let failTemplates = true;
  const searches: string[] = [];
  await page.route('**/api/sites?*', async (route) => {
    if (failSites) return route.fulfill({ status: 503, json: { error: 'unavailable' } });
    const query = new URL(route.request().url()).searchParams;
    const limit = Number(query.get('limit') ?? 50);
    expect(limit).toBeLessThanOrEqual(200);
    // the picker searches server-side (ADR 51): a word of the name starts with what was typed
    const q = query.get('q');
    if (q !== null) searches.push(q);
    const words = (q ?? '').toLowerCase().split(/\s+/).filter(Boolean);
    const hits = sites.filter((site) =>
      words.every((word) => site.name.toLowerCase().split(/\s+/).some((part) => part.startsWith(word))),
    );
    await route.fulfill({ json: { items: hits.slice(0, limit), total: hits.length, openCount: hits.length,
      next: hits.length > limit ? 'more' : null } });
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
  failSites = false;
  await page.getByRole('button', { name: 'Retry sites' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load checklists.' })).toBeVisible();
  failToday = false;
  await page.getByRole('button', { name: 'Retry checklists' }).click();
  await expect(page.getByText('Site 001 ·', { exact: false })).toBeVisible();
  // templates are the admin's tab, off the screen a manager works
  await page.getByRole('tab', { name: 'Templates' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load templates.' })).toBeVisible();
  failTemplates = false;
  await page.getByRole('button', { name: 'Retry templates' }).click();
  await expect(page.getByText('No templates yet.')).toBeVisible();
  await page.getByRole('tab', { name: 'Today' }).click();

  // the 201st site is one search away, not four pages of "load more"
  const picker = page.getByLabel('Checklist site');
  await picker.fill('201');
  await page.getByRole('option', { name: 'Site 201' }).click();
  await expect(page.getByText('Site 201 ·', { exact: false })).toBeVisible();
  expect(searches).toContain('201');
  await expect(page.getByRole('button', { name: 'Load more sites' })).toHaveCount(0);
});
