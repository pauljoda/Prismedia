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
  return page.locator('#product').evaluate((section) => {
    const paths = [section.querySelector('[data-white-light]')!, ...section.querySelectorAll('[data-spectrum-line]')];
    return paths.map((path) => Number.parseFloat(String(path.computedStyleMap().get('stroke-dashoffset'))));
  });
}

test('scrolling brings the white light in before the spectrum and reverses when scrolling back', async ({page}) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto('./');
  await expect(page.locator('#product')).toHaveAttribute('data-motion', 'true');
  await seekPrism(page, 0);
  await expect.poll(async () => (await drawnBeams(page))[0]).toBeCloseTo(1, 1);
  await seekPrism(page, 0.34);
  await expect.poll(async () => (await drawnBeams(page))[0]).toBeCloseTo(0, 1);
  expect((await drawnBeams(page)).slice(1).every((offset) => offset > 0.95)).toBe(true);
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
  const downloads = page.locator('#platforms').getByRole('link', {name: 'Download on the App Store'});
  await expect(downloads).toHaveCount(2);
  for (const download of await downloads.all()) {
    await expect(download).toHaveAttribute('href', 'https://apps.apple.com/us/app/prismedia/id6792944211');
  }
  await page.getByRole('contentinfo').getByRole('link', {name: 'Organize your folders'}).click();
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
  await expect(page.getByRole('img', {name: 'Browse a movie collection in the native Apple TV app'})).toBeVisible();
  await context.close();
});

test('the atmosphere responds to the pointer and respects system motion preferences', async ({page}) => {
  await page.goto('./');
  const atmosphere = page.locator('[data-atmosphere]');
  await expect(atmosphere).toHaveAttribute('data-active', 'true');
  await page.mouse.move(1200, 600);
  await expect.poll(() => atmosphere.evaluate((element) => element.style.getPropertyValue('--pointer-x'))).not.toBe('');
  await page.emulateMedia({reducedMotion: 'reduce'});
  await expect(atmosphere).toHaveAttribute('data-active', 'false');
  expect(await atmosphere.evaluate((element) => element.style.getPropertyValue('--pointer-x'))).toBe('');
  await expect(page.getByRole('button', {name: /background motion/})).toHaveCount(0);
});

test('media branches can be explored with the keyboard', async ({page}) => {
  await page.goto('./');
  await seekPrism(page, 1);
  const movie = page.getByRole('link', {name: 'Movies: A place for every film'});
  await movie.focus();
  await expect(movie).toBeFocused();
  await movie.press('Enter');
  await expect(page).toHaveURL(/\/docs\/library\/videos$/);
});
test('hero actions lead to source, native downloads, and setup alongside all three device views', async ({page}) => {
  await page.goto('./');
  const hero = page.locator('header').filter({has: page.getByRole('heading', {level: 1})});
  const actions = hero.locator('a.marketing-glass');
  await expect(actions).toHaveText(['View on GitHub', 'App Store iPhone, iPad & Apple TV', 'Read the setup guide']);
  await expect(actions.first()).toHaveAttribute('href', 'https://github.com/pauljoda/Prismedia');
  await expect(actions.nth(1)).toHaveAttribute('href', 'https://apps.apple.com/us/app/prismedia/id6792944211');
  await expect(page.locator('a[href*="testflight.apple.com"]')).toHaveCount(0);
  await expect(page.locator('.marketing-prism')).toHaveCount(1);
  const tv = page.getByRole('img', {name: 'Browse a movie collection in the native Apple TV app'});
  await expect(tv).toBeVisible();
  await expect.poll(() => tv.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0);
  await expect(tv.locator('..')).toHaveAttribute('href', /\/tvos-movies-live\.webp$/);
  await expect(page.getByRole('img', {name: 'Browse a movie collection in the Prismedia web app'})).toBeVisible();
  await expect(page.getByRole('img', {name: 'Explore a book in the native iPhone app'})).toBeVisible();
});

test('narrow phones can open the menu without overlapping service links', async ({page}) => {
  await page.setViewportSize({width: 320, height: 720});
  await page.goto('./');
  const toggle = page.getByRole('button', {name: 'Toggle navigation bar'});
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  await expect(page.locator('.navbar-sidebar').getByRole('link', {name: /GitHub/})).toBeVisible();
  await expect(page.locator('.navbar-sidebar').getByRole('link', {name: /App Store/})).toBeVisible();
  await page.locator('.navbar-sidebar').getByRole('link', {name: 'How it works', exact: true}).click();
  await expect(page).toHaveURL(/#workflow$/);
  await expect.poll(() => page.locator('#workflow').evaluate((section) => Math.abs(section.getBoundingClientRect().top - 75))).toBeLessThan(2);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('device frames stay inside the showcase across screen sizes without captions', async ({page}) => {
  await page.goto('./');
  const showcase = page.locator('[aria-label="Prismedia on web, iPhone, and Apple TV"]');
  await expect(showcase.locator('figcaption')).toHaveCount(0);
  for (const width of [320, 390, 768, 1045, 1440, 1920]) {
    await page.setViewportSize({width, height: 1000});
    const contained = await showcase.evaluate((element) => {
      const container = element.getBoundingClientRect();
      return Array.from(element.querySelectorAll('figure')).every((figure) => {
        const frame = figure.getBoundingClientRect();
        return frame.left >= container.left && frame.right <= container.right;
      });
    });
    expect(contained, `Device frame outside the showcase at ${width}px`).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});
