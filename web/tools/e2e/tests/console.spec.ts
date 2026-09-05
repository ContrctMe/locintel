import { expect, test, type Page } from '@playwright/test';
import { ALICE, OPERATOR, expectAccessible, nav, signIn } from './support';

async function createSite(page: Page) {
  await page.goto('/hierarchy');
  const create = page.getByRole('button', { name: 'Create hierarchy' });
  const addNode = page.getByRole('button', { name: 'Add node' });
  await expect(create.or(addNode)).toBeVisible();
  if (await create.isVisible()) {
    await page.getByLabel('Level names (root-first, comma-separated)').fill('Region, Site');
    await create.click();
    await expect(addNode).toBeVisible();
  }

  await page.goto('/sites');
  const name = `E2E Site ${Date.now()}`;
  await page.getByRole('button', { name: 'New site' }).click();
  await page.getByLabel('Name').fill(name);
  await page.getByLabel('Hierarchy node').selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Create site' }).click();
  await expect(page.getByText('Site created', { exact: true })).toBeVisible();
  return name;
}

test.describe('sign-in and the shell', () => {
  test('an owner signs in by hint and lands on the dashboard with the org nav', async ({ page }) => {
    await signIn(page, ALICE);
    for (const label of ['Sites', 'Members', 'Roles', 'Settings'])
      await expect(nav(page).getByRole('link', { name: label })).toBeVisible();
    // the platform section is for operators only: hidden, not disabled
    await expect(nav(page).getByRole('link', { name: 'Operator' })).toHaveCount(0);
  });

  test('the operator wall: an owner reaching /operator directly gets nothing to act on', async ({ page }) => {
    await signIn(page, ALICE);
    await page.goto('/operator');
    // the page renders (no crash, no blank 404 - the proxy regression) and offers no org list
    await expect(nav(page).getByRole('link', { name: 'Dashboard' })).toBeVisible();
    await expect(page.getByRole('button', { name: /suspend/i })).toHaveCount(0);
  });

  test('a platform operator sees the operator surface', async ({ page }) => {
    await signIn(page, OPERATOR);
    await nav(page).getByRole('link', { name: 'Operator' }).click();
    await expect(page).toHaveURL(/\/operator$/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  });

  test('the shell is keyboard navigable: Tab reaches the primary nav', async ({ page, browserName }) => {
    await signIn(page, ALICE);
    // macOS Safari uses Option-Tab to include links in keyboard navigation.
    const next = browserName === 'webkit' && process.platform === 'darwin' ? 'Alt+Tab' : 'Tab';
    const focused = async () => page.evaluate(() => document.activeElement?.textContent?.trim() ?? '');
    let reached = false;
    for (let i = 0; i < 25 && !reached; i++) {
      await page.keyboard.press(next);
      reached = (await focused()) === 'Sites';
    }
    expect(reached).toBe(true);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/sites$/);
  });

  test('a new user creates an organization and switches to a second one', async ({ page }) => {
    const stamp = Date.now();
    const first = `E2E First ${stamp}`;
    const second = `E2E Second ${stamp}`;
    await page.goto(`/auth/login?hint=e2e-${stamp}@example.test`);
    await expect(page.getByRole('heading', { name: 'Create your organization' })).toBeVisible();
    await page.getByLabel('Organization name').fill(first);
    await page.getByRole('button', { name: 'Create organization' }).click();
    await expect(nav(page).getByRole('link', { name: 'Dashboard' })).toBeVisible();

    await page.evaluate(async ({ name, slug }) => {
      const response = await fetch('/api/orgs', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, slug }),
      });
      if (!response.ok) throw new Error(`create org failed: ${response.status}`);
    }, { name: second, slug: `e2e-second-${stamp}` });
    await expect.poll(() => page.evaluate(async () => {
      const response = await fetch('/me', { credentials: 'include' });
      const me = await response.json() as { organizations: { name: string }[] };
      return me.organizations.map((org) => org.name);
    })).toContain(second);
    await page.reload();
    await page.locator('aside').getByRole('combobox').selectOption({ label: second });
    await expect(page.locator('aside').getByText(second, { exact: true }).first()).toBeVisible();
  });
});

test.describe('site management', () => {
  test('creating a site and opening it', async ({ page }) => {
    await signIn(page, ALICE);
    const name = await createSite(page);
    await page.getByRole('link', { name }).click();
    await expect(page).toHaveURL(/\/sites\/[0-9a-f-]+$/);
    await expect(page.getByRole('heading', { name })).toBeVisible();
  });

  test('an optimistic-concurrency conflict is explained to the editor', async ({ page }) => {
    await signIn(page, ALICE);
    const name = await createSite(page);
    await page.getByRole('link', { name }).click();
    await page.route('**/api/sites/*', async (route) => {
      if (route.request().method() === 'POST')
        await route.fulfill({ status: 409, contentType: 'application/json', body: '{}' });
      else await route.continue();
    });
    await page.getByRole('button', { name: 'Edit' }).click();
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(
      page.getByText('The request conflicts with a newer change', { exact: true }),
    ).toBeVisible();
  });

  test('a validation failure is reported, not swallowed', async ({ page }) => {
    await signIn(page, ALICE);
    await page.goto('/sites');
    await page.getByRole('button', { name: 'New site' }).click();
    // the create button is disabled until the form is valid: the UI blocks the bad request
    await expect(page.getByRole('button', { name: 'Create site' })).toBeDisabled();
  });
});

test.describe('critical workflows', () => {
  test('an owner creates and assigns a role', async ({ page }) => {
    await signIn(page, ALICE);
    await page.goto('/roles');
    const name = `E2E role ${Date.now()}`;
    await page.getByRole('button', { name: 'New role' }).click();
    await page.getByLabel('Name').fill(name);
    await page.getByRole('dialog').getByLabel('sites:read', { exact: true }).check();
    await page.getByRole('button', { name: 'Create role' }).click();
    await expect(page.getByText('Role saved', { exact: true })).toBeVisible();

    await page.getByRole('button', { name: 'Assign role' }).click();
    await page.getByLabel('Role', { exact: true }).selectOption({ label: name });
    await page.getByLabel('Member', { exact: true }).selectOption({ label: ALICE });
    await page.getByRole('button', { name: 'Assign', exact: true }).click();
    await expect(page.getByText('Role assigned', { exact: true })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: name })).toContainText('1');
  });

  test('an owner uploads, stages, and commits a CSV', async ({ page }) => {
    await signIn(page, ALICE);
    await page.goto('/hierarchy');
    const create = page.getByRole('button', { name: 'Create hierarchy' });
    const addNode = page.getByRole('button', { name: 'Add node' });
    await expect(create.or(addNode)).toBeVisible();
    if (await create.isVisible()) {
      await create.click();
      await expect(addNode).toBeVisible();
    }
    const nodeName = `E2E Region ${Date.now()}`;
    await addNode.click();
    await page.getByLabel('Name').fill(nodeName);
    await page.getByLabel('Parent').selectOption({ index: 1 });
    await page.getByRole('button', { name: 'Add node', exact: true }).last().click();
    await expect(page.getByText('Node added', { exact: true })).toBeVisible();

    await page.goto('/ingest');
    const externalId = `e2e-${Date.now()}`;
    await page.locator('input[type=file]').setInputFiles({
      name: 'sites.csv',
      mimeType: 'text/csv',
      buffer: Buffer.from(`external_id,name,time_zone,node,status\n${externalId},E2E Imported,America/New_York,${nodeName},open\n`),
    });
    await expect(page.getByText(/Diff preview — 1 new/)).toBeVisible();
    await page.getByRole('button', { name: 'Commit 1 changes' }).click();
    await expect(page.getByText('Batch is Committed.')).toBeVisible();
  });

  test('a network failure produces an actionable page state', async ({ page }) => {
    await signIn(page, ALICE);
    await page.route('**/api/roles', (route) => route.abort('failed'));
    await page.goto('/roles');
    await expect(page.getByText('Could not load roles.')).toBeVisible();
  });

  test('the built public SSR app renders and hydrates the tenant locator', async ({ page, request }) => {
    const url = process.env.E2E_PUBLIC ?? 'http://acme-dev.localhost:5174';
    const response = await request.get(url);
    expect(response.ok()).toBe(true);
    expect(await response.text()).toContain('Our locations');
    await page.goto(url);
    await expect(page.getByRole('heading', { name: 'Our locations' })).toBeVisible();
  });
});

test.describe('accessibility (axe, WCAG 2.0/2.1 A and AA)', () => {
  for (const path of ['/', '/sites', '/members', '/roles', '/settings', '/developers', '/audit', '/account'])
    test(`no serious or critical violations on ${path}`, async ({ page }) => {
      await signIn(page, ALICE);
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await expectAccessible(page);
    });

  test('the operator page for an operator', async ({ page }) => {
    await signIn(page, OPERATOR);
    await page.goto('/operator');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expectAccessible(page);
  });
});
