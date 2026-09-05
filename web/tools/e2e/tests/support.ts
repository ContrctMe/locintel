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

/** No serious or critical accessibility violations on the current page. */
export async function expectAccessible(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(
    blocking.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([]);
}
