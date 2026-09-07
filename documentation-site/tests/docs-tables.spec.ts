import {expect, test} from '@playwright/test';

test('short documentation tables fill their frame on desktop and phones', async ({page}) => {
  await page.goto('docs/using/settings');
  const table = page.getByRole('table').first();
  for (const width of [1280, 768, 375]) {
    await page.setViewportSize({width, height: 900});
    const geometry = await table.evaluate(element => ({
      table: element.getBoundingClientRect().width,
      row: element.querySelector('tr')!.getBoundingClientRect().width,
    }));
    expect(Math.abs(geometry.table - geometry.row)).toBeLessThan(2);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});

test('wide reference tables scroll with the keyboard without widening the phone page', async ({page}) => {
  await page.setViewportSize({width: 320, height: 740});
  await page.goto('docs/plugins/manifest');
  const frame = page.getByRole('region', {name: 'Reference table', exact: true}).first();
  await frame.scrollIntoViewIfNeeded();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(await frame.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
  await frame.focus();
  await expect(frame).toBeFocused();
  await page.keyboard.press('ArrowRight');
  await expect.poll(() => frame.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  await expect(frame.getByRole('columnheader', {name: 'Notes', exact: true})).toBeAttached();
});
