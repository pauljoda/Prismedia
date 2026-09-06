import {expect, test} from '@playwright/test';
import {SITE_URL} from '../site-metadata';
import {readFile} from 'node:fs/promises';
import {dirname, resolve} from 'node:path';

test('published pages have distinct metadata, canonical URLs, and the shared manifest', async ({page, request}, testInfo) => {
  const sitemap = await request.get('sitemap.xml');
  expect(sitemap.ok()).toBe(true);
  const urls = await page.evaluate((xml) => Array.from(new DOMParser().parseFromString(xml, 'application/xml').querySelectorAll('loc'), (loc) => loc.textContent!), await sitemap.text());
  expect(urls.length).toBeGreaterThan(30);
  const titles = new Set<string>();
  const descriptions = new Set<string>();

  for (const url of urls) {
    expect(url.startsWith(SITE_URL)).toBe(true);
    expect(url).not.toMatch(/404|\?/);
    const response = await request.get(new URL(url).pathname);
    expect(response.status(), url).toBe(200);
    const head = await page.evaluate((html) => {
      const document = new DOMParser().parseFromString(html, 'text/html');
      const meta = (key: string) => document.querySelector(`meta[name="${key}"], meta[property="${key}"]`)?.getAttribute('content');
      return {
        title: document.title, description: meta('description'),
        ogTitle: meta('og:title'), ogDescription: meta('og:description'),
        twitterTitle: meta('twitter:title'), twitterDescription: meta('twitter:description'),
        image: meta('og:image'), robots: meta('robots'),
        canonicals: Array.from(document.querySelectorAll('link[rel="canonical"]'), (link) => link.getAttribute('href')),
        manifest: document.querySelector('link[rel="manifest"]')?.getAttribute('href'),
      };
    }, await response.text());
    expect(head.title, url).toBeTruthy();
    expect(head.description, url).toBeTruthy();
    expect(titles.has(head.title), `Duplicate title: ${url}`).toBe(false);
    expect(descriptions.has(head.description!), `Duplicate description: ${url}`).toBe(false);
    titles.add(head.title);
    descriptions.add(head.description!);
    expect(head.canonicals, url).toEqual([url]);
    expect(head.robots, url).not.toContain('noindex');
    expect(head.ogTitle, url).toBe(head.title);
    expect(head.ogDescription, url).toBe(head.description);
    // Any social override must describe this page, not repeat the homepage.
    if (head.twitterTitle) expect(head.twitterTitle, url).toBe(head.title);
    if (head.twitterDescription) expect(head.twitterDescription, url).toBe(head.description);
    expect(head.image, url).toBe(`${SITE_URL}img/prismedia-social-card.png`);
    expect(new URL(head.manifest!, url).href, url).toBe(`${SITE_URL}site.webmanifest`);
  }
  // GitHub Pages serves this artifact for missing routes. The local Docusaurus
  // server rewrites unknown paths to the SPA entry point instead.
  const notFound = await readFile(resolve(dirname(testInfo.config.configFile!), 'build/404.html'), 'utf8');
  const robots = await page.evaluate((html) => new DOMParser().parseFromString(html, 'text/html').querySelector('meta[name="robots"]')?.getAttribute('content'), notFound);
  expect(robots).toBe('noindex, nofollow');
});

test('manifest icons and shortcuts resolve correctly under the public site path', async ({request, page}) => {
  const response = await request.get('site.webmanifest');
  expect(response.ok()).toBe(true);
  const manifest = await response.json();
  const manifestUrl = `${SITE_URL}site.webmanifest`;
  for (const field of ['id', 'scope', 'start_url']) expect(new URL(manifest[field], manifestUrl).href).toBe(SITE_URL);
  expect(manifest.display).toBe('browser');
  expect(manifest.icons.some((icon: {purpose: string}) => icon.purpose === 'maskable')).toBe(true);
  for (const icon of manifest.icons) {
    const image = await request.get(new URL(icon.src, manifestUrl).pathname);
    expect(image.ok()).toBe(true);
    const bytes = await image.body();
    expect(`${bytes.readUInt32BE(16)}x${bytes.readUInt32BE(20)}`).toBe(icon.sizes);
  }
  for (const shortcut of manifest.shortcuts) {
    const url = new URL(shortcut.url, manifestUrl);
    expect(url.href.startsWith(SITE_URL)).toBe(true);
    expect((await request.get(url.pathname)).ok(), shortcut.name).toBe(true);
  }
  await page.goto('./');
  for (const rel of ['icon', 'apple-touch-icon']) {
    const urls = await page.locator(`head link[rel="${rel}"]`).evaluateAll((links) => links.map((link) => (link as HTMLLinkElement).href));
    expect(urls.length).toBeGreaterThan(0);
    for (const url of urls) expect((await request.get(url)).ok()).toBe(true);
  }
  const schema = await page.locator('script[type="application/ld+json"]').first().textContent();
  const graph = JSON.parse(schema!)['@graph'];
  expect(graph.map((item: {'@type': string}) => item['@type'])).toEqual(['WebSite', 'SoftwareApplication']);
  expect(graph.every((item: {url: string}) => item.url === SITE_URL)).toBe(true);
  const social = await request.get('img/prismedia-social-card.png');
  const bytes = await social.body();
  expect([bytes.readUInt32BE(16), bytes.readUInt32BE(20)]).toEqual([1200, 630]);
});
