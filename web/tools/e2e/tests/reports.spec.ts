import { expect, test } from '@playwright/test';
import { ALICE, expectAccessible, signIn } from './support';

test('reports capture a site, produce a downloadable PDF, and remain accessible in both themes', async ({ page }, testInfo) => {
  test.setTimeout(90_000);
  await signIn(page, ALICE);
  await page.goto('/hierarchy');
  const createHierarchy = page.getByRole('button', { name: 'Create hierarchy' });
  await expect(createHierarchy.or(page.getByRole('button', { name: 'Add node' }))).toBeVisible();
  if (await createHierarchy.isVisible()) {
    await page.getByLabel('Top level', { exact: true }).fill('Region');
    await page.getByLabel('Level 2', { exact: true }).fill('Site');
    await createHierarchy.click();
    await expect(page.getByRole('button', { name: 'Add node' })).toBeVisible();
  }
  const name = `Report Site ${Date.now()}`;
  await page.goto('/sites');
  await page.getByRole('button', { name: 'New site' }).click();
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Hierarchy node').click();
  await page.getByRole('listbox').getByRole('option').first().click();
  await page.getByRole('button', { name: 'Create site' }).click();
  await expect(page).toHaveURL(/\/sites\/[0-9a-f-]+$/);
  await page.goto('/sites');
  await page.getByRole('textbox', { name: 'Search sites', exact: true }).fill(name);
  const row = page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
  await row.getByRole('checkbox').check();
  await page.getByRole('button', { name: 'Generate report', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Generate site reports' });
  await expect(dialog.getByText(name, { exact: true })).toBeVisible();
  await expect(
    dialog.getByText('This run will use 1 of 1000 PDFs available this UTC month.', { exact: true }),
  ).toBeVisible();
  await dialog.getByRole('combobox', { name: 'Report type', exact: true }).focus();
  await page.keyboard.press('Shift+Tab');
  await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('combobox', { name: 'Report type', exact: true })).toBeFocused();
  await expectAccessible(page);
  await dialog.getByRole('button', { name: 'Generate report', exact: true }).click();
  await expect(page).toHaveURL(/\/reports\/[0-9a-f-]+$/);
  await expect(page.getByText('Completed · 1 of 1 PDFs ready', { exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText(/no report basemap configured/)).toBeVisible();
  await expect(page.getByText('999 of 1000 PDFs available', { exact: true })).toBeVisible({ timeout: 10_000 });
  await expect(page.getByText(/1 generated · 0 reserved · UTC month starting/)).toBeVisible({ timeout: 10_000 });
  await expectAccessible(page);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: testInfo.outputPath('report-completed.png'), fullPage: true });
  const download = page.waitForEvent('download');
  // The PDF is named for its site, not for its position in the run.
  const pdfButton = page.getByRole('button', { name: new RegExp(`^Download ${name} report `) });
  await pdfButton.click();
  const file = await download;
  expect(await file.failure()).toBeNull();
  await file.saveAs(testInfo.outputPath('site-report.pdf'));
  const runUrl = page.url();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Report run' })).toBeVisible();
  await expect(pdfButton).toBeVisible();
  await page.getByRole('link', { name, exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Site files', exact: true })).toBeVisible();
  const runLink = page.getByRole('link', { name: 'Report run', exact: true });
  await expect(runLink).toBeVisible();
  const fileRow = page.getByRole('row').filter({ has: runLink });
  const siteDownload = page.waitForEvent('download');
  await fileRow.getByRole('button', { name: 'Download', exact: true }).click();
  expect(await (await siteDownload).failure()).toBeNull();
  await expectAccessible(page);
  await runLink.click();
  await expect(page).toHaveURL(runUrl);
  await page.goto('/files');
  await page.getByRole('link', { name: 'Report run', exact: true }).click();
  await expect(page).toHaveURL(runUrl);
});
