import { expect, test } from '@playwright/test';
import { ALICE, nav, OPERATOR, signIn } from './support';

test('a pending or unavailable hierarchy never offers provisioning', async ({ page }) => {
  await signIn(page, ALICE);
  let release!: () => void;
  const held = new Promise<void>((resolve) => { release = resolve; });
  await page.route('**/api/hierarchy', async (route) => {
    await held;
    await route.fulfill({ status: 503, body: 'Hierarchy unavailable' });
  });
  await page.goto('/hierarchy');
  await expect(page.getByText('Loading hierarchy…', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Create hierarchy', exact: true })).toHaveCount(0);
  release();
  await expect(page.getByText('Could not load hierarchy.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Create hierarchy', exact: true })).toHaveCount(0);
});

test('operator selection resets entitlement drafts and follows refreshed lifecycle state', async ({ page }) => {
  await signIn(page, OPERATOR);
  const stamp = `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
  const names = [`Controls A ${stamp}`, `Controls B ${stamp}`];
  await page.evaluate(async ({ names, stamp }) => {
    for (const [index, name] of names.entries()) {
      const response = await fetch('/api/orgs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, slug: `controls-${index}-${stamp}` }),
      });
      if (!response.ok) throw new Error(`Create test org: ${response.status}`);
    }
  }, { names, stamp });
  await nav(page).getByRole('link', { name: 'Operator', exact: true }).click();
  const select = async (name: string) => {
    await page.getByRole('button').filter({ hasText: name }).click();
    await expect(page.getByLabel('Site limit', { exact: true })).toBeVisible();
  };
  await select(names[0]!);
  const limit = page.getByLabel('Site limit', { exact: true });
  await expect(limit).toHaveValue('100');
  await limit.fill('1234');
  await select(names[1]!);
  await expect(limit).toHaveValue('100');
  await select(names[0]!);
  await expect(limit).toHaveValue('100');
  await limit.fill('234');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toHaveCount(0);
  await select(names[1]!);
  await expect(limit).toHaveValue('100');
  await select(names[0]!);
  await expect(limit).toHaveValue('234');

  await page.route('**/api/operator/orgs/*/suspend', (route) =>
    route.fulfill({ status: 503, body: 'Lifecycle unavailable' }), { times: 1 });
  await page.getByRole('button', { name: 'Suspend', exact: true }).click();
  await expect(page.getByText('Lifecycle unavailable', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Suspend', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Suspend', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Reactivate', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Offboard', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Suspend', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Offboard', exact: true })).toBeDisabled();
});

test('role editor owns fresh drafts and preserves failed edits for retry', async ({ page }) => {
  await signIn(page, ALICE);
  await nav(page).getByRole('link', { name: 'Roles', exact: true }).click();
  await page.getByRole('button', { name: 'New role', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('button', { name: 'Create role', exact: true })).toBeDisabled();
  const name = `Editor ${Date.now()}`;
  await dialog.getByLabel('Name', { exact: true }).fill(name);
  await dialog.getByLabel('sites:read', { exact: true }).check();
  await dialog.getByRole('button', { name: 'Create role', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('row').filter({ hasText: name }).getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue(name);
  await expect(dialog.getByLabel('sites:read', { exact: true })).toBeChecked();
  await dialog.getByLabel('Name', { exact: true }).fill(`${name} revised`);
  await page.route('**/api/roles/*', async (route) => {
    if (route.request().method() === 'PUT')
      await route.fulfill({ status: 503, body: 'Role save unavailable' });
    else await route.continue();
  }, { times: 1 });
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByText('Role save unavailable', { exact: true })).toBeVisible();
  await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue(`${name} revised`);
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('cell', { name: `${name} revised`, exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'New role', exact: true }).click();
  await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue('');
  await expect(dialog.getByLabel('sites:read', { exact: true })).not.toBeChecked();
});

test('site hours and closures retain failed drafts and refresh their own data', async ({ page }) => {
  await signIn(page, ALICE);
  const name = `Hours ${Date.now()}`;
  const siteId = await page.evaluate(async (name) => {
    const hierarchyResponse = await fetch('/api/hierarchy');
    let nodeId: string;
    if (hierarchyResponse.status === 404) {
      const created = await fetch('/api/hierarchy', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: 'Hours test hierarchy', levels: ['Region', 'Site'] }),
      });
      if (!created.ok) throw new Error(`Create hierarchy: ${created.status}`);
      nodeId = (await created.json()).rootNodeId;
    } else {
      if (!hierarchyResponse.ok) throw new Error(`Hierarchy: ${hierarchyResponse.status}`);
      nodeId = (await hierarchyResponse.json()).nodes[0].id;
    }
    const response = await fetch('/api/sites', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, nodeId, timeZone: 'Etc/UTC' }),
    });
    if (!response.ok) throw new Error(`Site: ${response.status}`);
    return (await response.json()).id as string;
  }, name);
  await page.goto(`/sites/${siteId}`);
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
  await expect(page.getByText('No open windows in the next 7 days.', { exact: true })).toBeVisible();
  // Reproduce a successful read that arrives before the asynchronous rebuild.
  // The next read goes to the real projection; refreshing once is insufficient.
  await page.route(`**/api/sites/${siteId}/windows?days=7`, (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }), { times: 1 });
  await page.getByLabel('Name', { exact: true }).fill('Service hours');
  await page.getByRole('button', { name: 'Add hours', exact: true }).click();
  const schedule = page.getByRole('row').filter({ hasText: 'Service hours' });
  await expect(schedule).toContainText('09:00 – 17:00');
  await expect(page.getByText('9:00 AM – 5:00 PM', { exact: true }).first()).toBeVisible();

  const date = new Date(Date.now() + 2 * 86400_000).toISOString().slice(0, 10);
  await page.getByLabel('Close a day', { exact: true }).fill(date);
  await page.route('**/api/sites/*/closures', async (route) => {
    if (route.request().method() === 'POST')
      await route.fulfill({ status: 503, body: 'Closure unavailable' });
    else await route.continue();
  }, { times: 1 });
  await page.getByRole('button', { name: 'Close this day', exact: true }).click();
  await expect(page.getByText('Closure unavailable', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Close a day', { exact: true })).toHaveValue(date);
  await page.getByRole('button', { name: 'Close this day', exact: true }).click();
  await expect(page.getByLabel('Close a day', { exact: true })).toHaveValue('');
  await page.getByRole('button', { name: 'Reopen', exact: true }).click();
  await page.getByRole('button', { name: 'Reopen?', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Reopen', exact: true })).toHaveCount(0);

  await schedule.getByRole('button', { name: 'Remove', exact: true }).click();
  await schedule.getByRole('button', { name: 'Sure?', exact: true }).click();
  await expect(schedule).toHaveCount(0);
  await expect(page.getByText('No open windows in the next 7 days.', { exact: true })).toBeVisible();
});
