import AxeBuilder from '@axe-core/playwright';
import { expect, type Locator, type Page } from '@playwright/test';

export const ALICE = 'alice@acme.test'; // owner of the seeded org (DevBootstrap)
export const OPERATOR = 'operator@locintel.local'; // member of the platform org

/** The primary navigation (the desktop sidebar): pages link to the same places, so scope to it. */
export const nav = (page: Page): Locator => page.getByRole('navigation').first();

/** Password-less sign-in: the local provider's code IS the hint (LocalAuthProvider). */
export async function signIn(page: Page, email: string) {
  const response = await page.goto(`/auth/login?hint=${encodeURIComponent(email)}`);
  expect(response?.status() ?? 500, `Sign-in failed with HTTP ${response?.status()}`).toBeLessThan(400);
  await expect(nav(page).getByRole('link', { name: 'Dashboard' })).toBeVisible();
}

/**
 * No serious or critical accessibility violations on the current page, in
 * BOTH themes: the .dark class is the theme switch, and a token that is
 * readable on one ground can vanish on the other (the light "Closed" badge
 * did).
 */
export async function expectAccessible(page: Page) {
  const wasDark = await page.evaluate(() => document.documentElement.classList.contains('dark'));
  // colours transition when the theme flips; axe must sample the settled state
  const freeze = await page.addStyleTag({ content: '*, *::before, *::after { transition: none !important; animation: none !important; }' });
  for (const dark of [wasDark, !wasDark]) {
    await page.evaluate((on) => document.documentElement.classList.toggle('dark', on), dark);
    await page.waitForTimeout(50);
    // Base UI intentionally exposes focus sentinels for Safari/VoiceOver;
    // hiding them breaks focus trapping. Maintainer guidance:
    // https://github.com/mui/base-ui/issues/5237
    const results = await new AxeBuilder({ page })
      .exclude('span[data-base-ui-focus-guard]')
      .withTags(['wcag2a', 'wcag2aa']).analyze();
    const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
    expect(
      blocking.map((v) => `[${dark ? 'dark' : 'light'}] ${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
    ).toEqual([]);
  }
  await page.evaluate((on) => document.documentElement.classList.toggle('dark', on), wasDark);
  await freeze.evaluate((el) => (el as HTMLElement).remove());
}

/** Sign out through the account menu (the top bar's avatar), where sign-out lives on every page. */
export async function signOut(page: Page) {
  await page.getByRole('button', { name: 'Account menu' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
}
