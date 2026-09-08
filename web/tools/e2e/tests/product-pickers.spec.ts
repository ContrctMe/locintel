import { expect, test } from '@playwright/test';
import { ALICE, signIn } from './support';

const farSite = { id: '00000000-0000-0000-0000-000000000201', name: 'Site 201', city: null };

for (const [path, label] of [
  ['/incidents', 'Filter by site'],
  ['/analytics', 'Filter by site'],
  ['/alerts', 'Acknowledgement site'],
  ['/patrols', 'Patrol site'],
  ['/marketplace', 'Site'],
] as const) {
  test(`${path} can select site 201 and recover a failed search`, async ({ page }) => {
    await signIn(page, ALICE);
    let fail = true;
    await page.route('**/api/sites?*', async (route) => {
      const q = new URL(route.request().url()).searchParams;
      if (q.get('q') === '201' && fail) return route.fulfill({ status: 503, json: { error: 'search unavailable' } });
      return route.fulfill({ json: { items: q.get('q') === '201' ? [farSite] : [], total: 1, openCount: 1, next: null } });
    });
    await page.goto(path);
    if (path === '/marketplace') await page.getByRole('button', { name: 'New request', exact: true }).click();
    const picker = page.getByRole('combobox', { name: label, exact: true });
    await picker.fill('201');
    await expect(page.getByRole('alert').filter({ hasText: 'Could not search sites.' })).toBeVisible();
    fail = false;
    await page.getByRole('button', { name: 'Retry site search' }).click();
    await picker.click();
    await page.getByRole('option', { name: 'Site 201', exact: true }).click();
    await expect(picker).toHaveValue('Site 201');
    // The chosen record is kept even after the search input resets.
    await expect(page.getByRole('alert').filter({ hasText: 'Could not search sites.' })).toHaveCount(0);
  });
}
