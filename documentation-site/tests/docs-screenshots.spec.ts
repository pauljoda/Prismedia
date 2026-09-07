import {expect, test} from '@playwright/test';
import {readFileSync, readdirSync} from 'node:fs';
import {dirname, resolve} from 'node:path';

test('every illustrated guide loads its captures with accurate dimensions and full-size links', async ({page}, testInfo) => {
  const docsDirectory = resolve(dirname(testInfo.config.configFile!), 'docs');
  const illustratedGuides = readdirSync(docsDirectory, {recursive: true})
    .filter((file): file is string => typeof file === 'string' && file.endsWith('.md'))
    .filter(file => readFileSync(`${docsDirectory}/${file}`, 'utf8').includes('<DocScreenshot '));
  for (const guide of illustratedGuides) {
    await page.goto(`docs/${guide.replace(/\.md$/, '')}`);
    const links = page.getByRole('link', {name: /^Open full-size screenshot:/});
    expect(await links.count(), guide).toBeGreaterThan(0);
    for (const link of await links.all()) {
      const img = link.getByRole('img');
      await img.scrollIntoViewIfNeeded();
      await expect.poll(() => img.evaluate((node: HTMLImageElement) => node.complete && node.naturalWidth > 0)).toBe(true);
      const dimensions = await img.evaluate((node: HTMLImageElement) => ({
        width: node.naturalWidth,
        height: node.naturalHeight,
        declaredWidth: Number(node.getAttribute('width')),
        declaredHeight: Number(node.getAttribute('height')),
      }));
      expect(dimensions.width).toBe(dimensions.declaredWidth);
      expect(dimensions.height).toBe(dimensions.declaredHeight);
      await expect(link).toHaveAttribute('href', (await img.getAttribute('src'))!);
      await expect(link).toHaveAttribute('href', /^\/Prismedia\/img\//);
      await expect(link).toHaveAttribute('target', '_blank');
    }
  }
});

test('phone captures fit a narrow article and full-size images are keyboard accessible', async ({page}) => {
  await page.setViewportSize({width: 320, height: 740});
  await page.goto('docs/using/browsing');
  const mobileCaptures = page.locator('figure:has(img[src*="/mobile-"])');
  await expect(mobileCaptures).toHaveCount(3);
  for (const figure of await mobileCaptures.all()) {
    await figure.scrollIntoViewIfNeeded();
    const box = await figure.boundingBox();
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(320);
  }
  const link = mobileCaptures.first().getByRole('link');
  await link.focus();
  await expect(link).toBeFocused();
  const popupPromise = page.waitForEvent('popup');
  await page.keyboard.press('Enter');
  const popup = await popupPromise;
  await popup.waitForLoadState();
  await expect(popup).toHaveURL(/\/Prismedia\/img\/screenshots\/mobile-dashboard\.webp$/);
  await expect(popup.locator('img')).toBeVisible();
  await popup.close();
});
