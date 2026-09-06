import {chromium} from '@playwright/test';
import {fileURLToPath} from 'node:url';

// Render the code-authored brand artwork at the standard social-card dimensions.
const browser = await chromium.launch({channel: process.env.PLAYWRIGHT_CHANNEL});
try {
  const page = await browser.newPage({viewport: {width: 1200, height: 630}, deviceScaleFactor: 1});
  await page.goto(new URL('../branding/social-card.html', import.meta.url).href);
  await page.evaluate(async () => {
    await document.fonts.ready;
    await Promise.all(Array.from(document.images, (image) => image.decode()));
  });
  await page.screenshot({path: fileURLToPath(new URL('../static/img/prismedia-social-card.png', import.meta.url))});
} finally {
  await browser.close();
}
