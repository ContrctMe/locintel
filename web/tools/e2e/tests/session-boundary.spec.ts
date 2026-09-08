import { expect, test, type Page } from '@playwright/test';
import { ALICE, nav, OPERATOR, signIn } from './support';

async function request(page: Page, path: string, body?: unknown) {
  return page.evaluate(async ({ path, body }) => {
    const response = await fetch(path, {
      method: body === undefined ? 'GET' : 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) throw new Error(`${path}: ${response.status}`);
    return response.json();
  }, { path, body });
}

async function switchTo(page: Page, name: string) {
  await page.locator('aside').getByRole('combobox', { name: 'Active organization' }).selectOption({ label: name });
  await expect(page.locator('aside').getByText(name, { exact: true }).first()).toBeVisible();
}

async function twoOrganizations(page: Page) {
  const stamp = `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
  const a = `Tenant A ${stamp}`;
  const b = `Tenant B ${stamp}`;
  await page.goto(`/auth/login?hint=${stamp}@example.test`);
  await page.getByLabel('Organization name').fill(a);
  await page.getByRole('button', { name: 'Create organization' }).click();
  await expect(nav(page).getByRole('link', { name: 'Sites', exact: true })).toBeVisible();
  const seed = async (name: string) => {
    const hierarchy = await request(page, '/api/hierarchy', { name: `${name} root`, levels: ['Region', 'Site'] });
    await request(page, '/api/sites', { name, nodeId: hierarchy.rootNodeId, timeZone: 'Etc/UTC' });
  };
  await seed('A-only site');
  await request(page, '/api/orgs', { name: b, slug: `b-${stamp}` });
  await expect.poll(async () => (await request(page, '/me')).organizations.map((o: { name: string }) => o.name)).toContain(b);
  await page.reload();
  await switchTo(page, b);
  await seed('B-only site');
  await switchTo(page, a);
  await nav(page).getByRole('link', { name: 'Sites', exact: true }).click();
  await expect(page.getByRole('link', { name: 'A-only site' })).toBeVisible();
  return { a, b };
}

test('tenant switch clears cached data and form drafts before a delayed new read', async ({ page }) => {
  const { a, b } = await twoOrganizations(page);
  await page.getByRole('button', { name: 'New site' }).click();
  await page.getByLabel('Name', { exact: true }).fill('A-private draft');
  await page.keyboard.press('Escape');
  let release!: () => void;
  const held = new Promise<void>((resolve) => { release = resolve; });
  await page.route('**/api/sites?**', async (route) => {
    const response = await route.fetch();
    await held;
    await route.fulfill({ response });
  });
  await switchTo(page, b);
  await expect(page.getByText('Loading sites…')).toBeVisible();
  await expect(page.getByRole('link', { name: 'A-only site' })).toHaveCount(0);
  release();
  await expect(page.getByRole('link', { name: 'B-only site' })).toBeVisible();
  await page.unroute('**/api/sites?**');
  await page.getByRole('button', { name: 'New site' }).click();
  await expect(page.getByLabel('Name', { exact: true })).toHaveValue('');
  await page.keyboard.press('Escape');
  for (const org of [a, b, a]) {
    await switchTo(page, org);
    await expect(page.getByRole('link', { name: org === a ? 'A-only site' : 'B-only site' })).toBeVisible();
    await expect(page.getByRole('link', { name: org === a ? 'B-only site' : 'A-only site' })).toHaveCount(0);
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await expect(page.getByRole('combobox', { name: 'Active organization' })).toBeVisible();
});

test('a late previous-tenant response cannot repopulate the new cache', async ({ page }) => {
  const { b } = await twoOrganizations(page);
  let release!: () => void;
  let captured!: () => void;
  let fulfilled!: () => void;
  const held = new Promise<void>((resolve) => { release = resolve; });
  const started = new Promise<void>((resolve) => { captured = resolve; });
  const finished = new Promise<void>((resolve) => { fulfilled = resolve; });
  await page.route('**/api/hierarchy', async (route) => {
    const response = await route.fetch();
    captured();
    await held;
    await route.fulfill({ response });
    fulfilled();
  }, { times: 1 });
  await nav(page).getByRole('link', { name: 'Hierarchy', exact: true }).click();
  await started;
  await switchTo(page, b);
  await expect(page.getByText('B-only site root', { exact: true })).toBeVisible();
  release();
  await finished;
  await expect(page.getByText('B-only site root', { exact: true })).toBeVisible();
  await expect(page.getByText('A-only site root', { exact: true })).toHaveCount(0);
  await nav(page).getByRole('link', { name: 'Sites', exact: true }).click();
  await expect(page.getByRole('link', { name: 'B-only site' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'A-only site' })).toHaveCount(0);
});

test('a failed switch response re-resolves the cookie and a failed new read shows no old data', async ({ page }) => {
  const { b } = await twoOrganizations(page);
  await page.route('**/auth/switch-org', async (route) => {
    const response = await route.fetch(); // Cookie changed, but response body failed.
    await route.fulfill({ response, status: 503, body: 'Switch response unavailable' });
  });
  await page.route('**/api/sites?**', (route) => route.abort());
  await switchTo(page, b);
  await expect(page.getByRole('alert')).toContainText('Switch response unavailable');
  await expect(page.getByText('Could not load sites.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'A-only site' })).toHaveCount(0);
});

test('an in-flight tenant mutation finishes before switching the cookie', async ({ page }) => {
  const { a, b } = await twoOrganizations(page);
  let release!: () => void;
  let captured!: () => void;
  const held = new Promise<void>((resolve) => { release = resolve; });
  const started = new Promise<void>((resolve) => { captured = resolve; });
  let switches = 0;
  await page.route('**/auth/switch-org', async (route) => { switches++; await route.continue(); });
  await page.route('**/api/sites', async (route) => {
    captured();
    await held;
    await route.continue();
  });
  await page.getByRole('button', { name: 'New site' }).click();
  await page.getByLabel('Name', { exact: true }).fill('Pending A write');
  await page.getByLabel('Hierarchy node').selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Create site', exact: true }).click();
  await started;
  await page.keyboard.press('Escape');
  await page.locator('aside').getByRole('combobox').selectOption({ label: b });
  await expect(page.getByRole('status')).toContainText('Changing session');
  expect(switches).toBe(0);
  release();
  await expect(page.getByRole('link', { name: 'B-only site' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Pending A write' })).toHaveCount(0);
  await switchTo(page, a);
  await expect(page.getByRole('link', { name: 'Pending A write' })).toBeVisible();
});

test('a stalled write releases a session transition with an outcome warning and no retry', async ({ page }) => {
  const { a, b } = await twoOrganizations(page);
  // Expire the native deadline deterministically; keep real requests, cookies,
  // and database effects. The transport unit test asserts the production duration.
  await page.evaluate(() => {
    const timeout = AbortSignal.timeout.bind(AbortSignal);
    AbortSignal.timeout = (milliseconds) => {
      const controller = new AbortController();
      window.addEventListener('test:expire-network', () => {
        controller.abort(new DOMException('deadline', 'TimeoutError'));
      }, { once: true });
      return AbortSignal.any([timeout(milliseconds), controller.signal]);
    };
  });
  let release!: () => void;
  const held = new Promise<void>((resolve) => { release = resolve; });
  let committed!: () => void;
  const saved = new Promise<void>((resolve) => { committed = resolve; });
  let writes = 0;
  await page.route('**/api/sites', async (route) => {
    writes++;
    const response = await route.fetch();
    committed();
    await held;
    await route.fulfill({ response });
  });
  try {
    await page.getByRole('button', { name: 'New site' }).click();
    await page.getByLabel('Name', { exact: true }).fill('Uncertain A write');
    await page.getByLabel('Hierarchy node').selectOption({ index: 1 });
    await page.getByRole('button', { name: 'Create site', exact: true }).click();
    await saved;
    await page.keyboard.press('Escape');
    await page.locator('aside').getByRole('combobox').selectOption({ label: b });
    await expect(page.getByRole('status')).toContainText('Changing session');
    await page.evaluate(() => window.dispatchEvent(new Event('test:expire-network')));
    await expect(page.getByRole('link', { name: 'B-only site' })).toBeVisible();
    await expect(page.getByRole('alert')).toContainText('may have completed');
    await expect(page.getByRole('link', { name: 'Uncertain A write' })).toHaveCount(0);
    expect(writes).toBe(1);
    release();
    await switchTo(page, a);
    await expect(page.getByRole('link', { name: 'Uncertain A write' })).toHaveCount(1);
  } finally {
    release();
  }
});

test('session changes abort the previous tenant network read', async ({ page }) => {
  const { b } = await twoOrganizations(page);
  await page.addInitScript(() => {
    const observations: { aborted: boolean; settled?: string }[] = [];
    Object.assign(window, { cancellationProbe: observations });
    const nativeFetch = window.fetch.bind(window);
    window.fetch = (input, init) => {
      if (!String(input).includes('/api/sites?')) return nativeFetch(input, init);
      const observation: typeof observations[number] = { aborted: init?.signal?.aborted ?? false };
      observations.push(observation);
      init?.signal?.addEventListener('abort', () => { observation.aborted = true; });
      const request = nativeFetch(input, init);
      void request.then((response) => {
        observation.settled = `response:${response.status}`;
      }, (error) => {
        observation.settled = error instanceof Error ? error.name : String(error);
      });
      return request;
    };
  });
  let release!: () => void;
  const held = new Promise<void>((resolve) => { release = resolve; });
  let captured!: () => void;
  const started = new Promise<void>((resolve) => { captured = resolve; });
  let first = true;
  await page.route('**/api/sites?**', async (route) => {
    if (!first) return route.continue();
    first = false;
    captured();
    await held;
    await route.continue();
  });
  try {
    await page.reload();
    await started;
    const firstRead = () => page.evaluate(() =>
      (window as unknown as { cancellationProbe: { aborted: boolean; settled?: string }[] }).cancellationProbe[0]);
    expect(await firstRead()).toEqual({ aborted: false });
    await page.locator('aside').getByRole('combobox').selectOption({ label: b });
    // Assert native fetch cancellation, not WebKit's interception event timing.
    await expect.poll(firstRead).toEqual({ aborted: true, settled: 'AbortError' });
    await expect(page.getByRole('link', { name: 'B-only site' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'A-only site' })).toHaveCount(0);
  } finally {
    release();
  }
});

test('missing session headers block the console and allow recovery after repair', async ({ page }) => {
  await signIn(page, ALICE);
  await page.route('**/me', async (route) => {
    const response = await route.fetch();
    const headers = response.headers();
    delete headers['x-locintel-session-context'];
    await route.fulfill({ response, headers });
  });
  await page.goto('/sites');
  await expect(page.getByRole('alert')).toContainText('Session verification unavailable');
  await expect(page.getByRole('button', { name: 'New site' })).toHaveCount(0);
  await page.unroute('**/me');
  await page.getByRole('button', { name: 'Retry session verification' }).click();
  await expect(page.getByRole('button', { name: 'New site' })).toBeVisible();
});

test('another tab discards tenant data and drafts after a shared-cookie switch', async ({ page, context }) => {
  const { b } = await twoOrganizations(page);
  const other = await context.newPage();
  await other.goto('/sites');
  await expect(other.getByRole('link', { name: 'A-only site' })).toBeVisible();
  await other.getByRole('button', { name: 'New site' }).click();
  await other.getByLabel('Name', { exact: true }).fill('Private A draft');
  await switchTo(page, b);
  const sharedSession = await request(other, '/me');
  expect(sharedSession.organizations.find((org: { id: string }) => org.id === sharedSession.activeOrg).name).toBe(b);
  await expect(other.getByRole('link', { name: 'B-only site' })).toBeVisible();
  await expect(other.getByRole('link', { name: 'A-only site' })).toHaveCount(0);
  await expect(other.getByRole('dialog')).toHaveCount(0);
  await other.getByRole('button', { name: 'New site' }).click();
  await expect(other.getByLabel('Name', { exact: true })).toHaveValue('');
  await other.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(other.getByRole('heading', { name: 'LocIntel Console' })).toBeVisible();
  await expect(other.getByRole('link', { name: 'B-only site' })).toHaveCount(0);
});

test('a stale-tab role draft cannot be written under a changed session cookie', async ({ page, context }) => {
  const { b } = await twoOrganizations(page);
  const other = await context.newPage();
  await other.goto('/roles');
  await other.getByRole('button', { name: 'New role', exact: true }).click();
  await other.getByRole('dialog').getByLabel('Name', { exact: true }).fill('Private A role');
  await other.getByRole('dialog').getByRole('checkbox', { name: '*:* (everything)', exact: true }).check();
  const session = await request(page, '/me');
  const target = session.organizations.find((org: { name: string }) => org.name === b);
  // Model a cookie change outside this tab's notification path (another app or
  // a response racing tab synchronization). The server must reject stale intent.
  expect(await page.evaluate(async (orgId) => (await fetch('/auth/switch-org', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ orgId }),
  })).status, target.id)).toBe(204);
  const write = other.waitForResponse((response) =>
    response.url().endsWith('/api/roles') && response.request().method() === 'POST');
  await other.getByRole('button', { name: 'Create role', exact: true }).click();
  await write;
  const roles = await request(page, '/api/roles');
  expect(roles.some((role: { name: string }) => role.name === 'Private A role')).toBe(false);
});

test('impersonation and logout discard the previous session tree', async ({ page }) => {
  await signIn(page, OPERATOR);
  const org = (await request(page, '/api/operator/orgs')).find((o: { isPlatform: boolean }) => !o.isPlatform);
  await nav(page).getByRole('link', { name: 'Operator', exact: true }).click();
  await page.getByRole('button').filter({ hasText: org.name }).click();
  await page.getByRole('button', { name: 'Impersonate', exact: true }).click();
  await expect(page.getByText('Support session:', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Stop impersonating' }).click();
  await expect(nav(page).getByRole('link', { name: 'Operator', exact: true })).toBeVisible();
  await expect(page.getByText('Support session:', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'LocIntel Console' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Operator' })).toHaveCount(0);
});

test('refocusing a tab detects an out-of-band cookie change and discards its draft', async ({ page, context }) => {
  const { b } = await twoOrganizations(page);
  const other = await context.newPage();
  await other.goto('/sites');
  await expect(other.getByRole('link', { name: 'A-only site' })).toBeVisible();
  await other.getByRole('button', { name: 'New site' }).click();
  await other.getByLabel('Name', { exact: true }).fill('Private A draft');
  await page.bringToFront();
  const session = await request(page, '/me');
  const target = session.organizations.find((org: { name: string }) => org.name === b);
  expect(await page.evaluate(async (orgId) => (await fetch('/auth/switch-org', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ orgId }),
  })).status, target.id)).toBe(204);
  await other.bringToFront();
  // Headless Chromium keeps both pages visible/focused and bringToFront emits
  // neither event. Exercise the browser event contract explicitly, not a reload.
  await other.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(other.getByRole('link', { name: 'B-only site' })).toBeVisible();
  await expect(other.getByRole('dialog')).toHaveCount(0);
  await expect(other.getByRole('link', { name: 'A-only site' })).toHaveCount(0);
});

test('a fresh login in another tab refreshes the previous identity', async ({ page, context }) => {
  // The second callback is rate-limited under the existing cookie's principal.
  // Give this test its own first identity/org instead of prior tests' budgets.
  const first = `fresh-login-${Date.now()}-${Math.random().toString(36).slice(2, 7)}@example.test`;
  await page.goto(`/auth/login?hint=${encodeURIComponent(first)}`);
  await page.getByLabel('Organization name').fill(`Fresh login ${Date.now()}`);
  await page.getByRole('button', { name: 'Create organization' }).click();
  await expect(nav(page).getByRole('link', { name: 'Dashboard' })).toBeVisible();
  const other = await context.newPage();
  await signIn(other, OPERATOR);
  await expect(nav(page).getByRole('link', { name: 'Operator', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: first, exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: OPERATOR, exact: true })).toBeVisible();
});
