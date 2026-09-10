import {expect, test} from '@playwright/test';

test('search preserves a query entered before the page finishes initializing', async ({page}) => {
  await page.setViewportSize({width: 375, height: 812});
  let resumeScripts!: () => void;
  const scriptsReady = new Promise<void>(resolve => { resumeScripts = resolve; });
  await page.route('**/assets/js/*.js', async route => {
    await scriptsReady;
    await route.continue();
  });
  try {
    await page.goto('docs/intro', {waitUntil: 'commit'});
    const search = page.locator('.navbar').getByLabel('Search', {exact: true});
    await search.fill('reader settings');
    resumeScripts();
    await page.waitForLoadState('load');
    await expect(page.getByRole('option').first()).toContainText('Native Reader Settings');
    await expect(search).toHaveValue('reader settings');
  } finally {
    resumeScripts();
  }
});

test('documentation search uses the local index and opens a guide with the keyboard', async ({page}) => {
  const indexResponses: number[] = [];
  page.on('response', response => {
    if (/search-index-.*\.json/.test(response.url())) indexResponses.push(response.status());
  });
  await page.goto('docs/intro');
  const search = page.locator('.navbar').getByLabel('Search', {exact: true});
  await expect(search).toBeVisible();
  await page.keyboard.press('ControlOrMeta+k');
  await expect(search).toBeFocused();
  await search.fill('organize folders');
  await expect(page.getByRole('option').first()).toContainText('Organize Your Media Folders');
  await search.press('Enter');
  await expect(page).toHaveURL(/\/Prismedia\/docs\/getting-started\/organize-folders/);
  await expect(page.getByRole('heading', {name: 'Organize Your Media Folders', exact: true})).toBeVisible();
  expect(indexResponses.length).toBeGreaterThan(0);
  expect(indexResponses.every(status => status === 200)).toBe(true);
  await page.goto('./');
  await expect(page.locator('.navbar').getByLabel('Search', {exact: true})).toHaveCount(0);
});

test('mobile search stays inside the viewport and offers full results and an empty state', async ({page}) => {
  await page.setViewportSize({width: 375, height: 812});
  await page.goto('docs/intro');
  const search = page.locator('.navbar').getByLabel('Search', {exact: true});
  await search.fill('reader settings');
  const result = page.getByRole('option').first();
  await expect(result).toBeVisible();
  const box = await result.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(375);
  await page.getByRole('link', {name: 'See all results'}).click();
  await expect(page).toHaveURL(/\/Prismedia\/search\?/);
  await expect(page.getByRole('article').getByRole('link', {name: 'Native Reader Settings', exact: true}).first()).toBeVisible();
  await search.fill('zzzznonexistentguide');
  await expect(page.getByText('No results', {exact: true})).toBeVisible();
});

test('search pages are excluded from indexing and the compact desktop header remains usable', async ({page, request}) => {
  const sitemap = await request.get('sitemap.xml');
  expect(await sitemap.text()).not.toMatch(/<loc>[^<]*\/search<\/loc>/);
  await page.goto('search');
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', 'noindex, follow');
  await page.setViewportSize({width: 997, height: 800});
  await page.goto('docs/intro');
  const navbar = await page.locator('.navbar').boundingBox();
  for (const element of await page.locator('.navbar__brand, .navbar__link:visible, .navbar__search-input').all()) {
    const box = await element.boundingBox();
    expect(box!.y).toBeGreaterThanOrEqual(navbar!.y);
    expect(box!.y + box!.height).toBeLessThanOrEqual(navbar!.y + navbar!.height);
  }
});
