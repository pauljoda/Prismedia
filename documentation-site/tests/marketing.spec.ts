import {expect, test, type Page} from '@playwright/test';

async function seekPrism(page: Page, progress: number) {
  await page.locator('#product').evaluate((section, value) => {
    const start = section.getBoundingClientRect().top + window.scrollY - 60;
    const distance = section.getBoundingClientRect().height - window.innerHeight + 60;
    window.scrollTo({top: start + distance * value, behavior: 'instant'});
  }, progress);
}

async function drawnBeams(page: Page) {
  // Typed OM resolves calc() before parsing, unlike strokeDashoffset's CSS string.
  return page.locator('#product svg path[pathLength]').evaluateAll((paths) =>
    paths.map((path) => Number.parseFloat(String(path.computedStyleMap().get('stroke-dashoffset')))),
  );
}

test('scrolling brings the white light in before the spectrum and reverses when scrolling back', async ({page}) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto('./');
  await expect(page.locator('#product')).toHaveAttribute('data-motion', 'true');
  await seekPrism(page, 0);
  await expect.poll(async () => (await drawnBeams(page))[0]).toBeCloseTo(1, 1);
  await seekPrism(page, 0.26);
  await expect.poll(async () => (await drawnBeams(page))[0]).toBeCloseTo(0, 1);
  expect((await drawnBeams(page)).slice(3).every((offset) => offset > 0.95)).toBe(true);
  await seekPrism(page, 1);
  await expect.poll(async () => (await drawnBeams(page)).every((offset) => offset < 0.01)).toBe(true);
  await seekPrism(page, 0);
  await expect.poll(async () => (await drawnBeams(page))[0]).toBeCloseTo(1, 1);
  await page.getByRole('link', {name: 'Explore the experiences', exact: true}).click();
  await expect(page).toHaveURL(/#experiences$/);
  expect(errors).toEqual([]);
});

test('reduced motion keeps the complete diagram and removes the pinned scrolling section', async ({page}) => {
  await page.emulateMedia({reducedMotion: 'reduce'});
  await page.goto('./');
  await expect(page.locator('#product')).toHaveAttribute('data-motion', 'false');
  expect((await drawnBeams(page)).every((offset) => offset === 0)).toBe(true);
  expect(await page.locator('#product > div').evaluate((element) => getComputedStyle(element).position)).not.toBe('sticky');
  // Changing the preference while the page is open also restores the complete illustration.
  await page.emulateMedia({reducedMotion: 'no-preference'});
  await expect(page.locator('#product')).toHaveAttribute('data-motion', 'true');
  await seekPrism(page, 0);
  await page.emulateMedia({reducedMotion: 'reduce'});
  await expect(page.locator('#product')).toHaveAttribute('data-motion', 'false');
  await expect.poll(async () => (await drawnBeams(page)).every((offset) => offset === 0)).toBe(true);
});

test('phone layout keeps the media labels, setup links, and folder tables within the screen', async ({page}) => {
  await page.setViewportSize({width: 390, height: 844});
  await page.goto('./');
  await expect(page.locator('#product')).toHaveAttribute('data-motion', 'false');
  expect((await drawnBeams(page)).every((offset) => offset === 0)).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await expect(page.getByRole('link', {name: 'Get the Apple TV app'})).toHaveAttribute('href', 'https://apps.apple.com/us/app/prismedia/id6792944211');
  await page.getByRole('link', {name: 'Understand your folders'}).click();
  await expect(page.getByRole('heading', {level: 1})).toHaveText('Organize Your Media Folders');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('the page and complete prism remain useful without JavaScript', async ({browser}) => {
  const context = await browser.newContext({javaScriptEnabled: false});
  const page = await context.newPage();
  await page.goto('http://127.0.0.1:3001/Prismedia/');
  await expect(page.getByRole('heading', {level: 1})).toHaveText('A clear home for all your media.');
  expect((await drawnBeams(page)).every((offset) => offset === 0)).toBe(true);
  await expect(page.getByRole('link', {name: 'Read the setup guide'})).toBeVisible();
  await context.close();
});
