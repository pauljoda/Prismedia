# Prismedia Documentation Site

This Docusaurus site builds the static Prismedia documentation published to GitHub Pages.

## Local development

```bash
pnpm docs:dev
```

The development server uses the repository root scripts and serves the site at `http://localhost:3000/Prismedia/` unless a different port is passed.

## Production build

```bash
pnpm docs:build
```

## Serve the production build

```bash
pnpm docs:serve
```

## Validate

```bash
pnpm docs:check
```

The marketing page's browser checks cover the scroll sequence, reduced motion,
device previews, keyboard navigation through media branches, mobile
layout, folder-guide navigation, and the JavaScript-disabled fallback.
Build the site first with `pnpm docs:check`, then run:

```bash
pnpm exec playwright test --config documentation-site/playwright.config.mts
```

The test runner serves the production build on port 3001. It uses Playwright's
Chromium by default; set `PLAYWRIGHT_CHANNEL=chrome` to use an installed Google
Chrome instead.

## Marketing design

The homepage uses Prismedia's shared typography and neutral surfaces. Media
markers and the prism illustration read their colors from the generated entity
definitions and shared UI color tokens, so the site follows the app's spectrum.
Keep instructional copy and real product images central. The prism is enhanced
with scroll motion on larger screens; smaller screens, reduced-motion settings,
and pages without JavaScript show the complete illustration.

The spectrum atmosphere follows the app's background treatment. It drifts slowly
and responds to a mouse or trackpad. Motion stops when the page is hidden, outside
the viewport, or reduced motion is requested. The main GitHub action carries color
inside its glass surface; other actions use neutral surfaces and familiar service
icons. The site has a fixed dark theme.
The prism illustration uses the published logo artwork, a continuous incoming
light path, and one consistent color per media branch. The hero layers real web, iPhone,
and Apple TV views; each screenshot links to the full-resolution image.

Deployment is handled by `.github/workflows/documentation-site.yml`.
The manual publishing workflow validates release metadata, builds the site, and runs the
browser and search-metadata checks before uploading the Pages artifact.

## Website identity and search metadata

`site-metadata.ts` owns the production origin, base path, homepage description,
and social artwork identity. Docusaurus generates canonical URLs, page-specific
Open Graph titles and descriptions, and `sitemap.xml`. Every documentation page
should have its own frontmatter description. Do not add global Twitter title or
description overrides: those replace the page-specific Open Graph fallback when
a guide is shared.

The shared `site.webmanifest` describes the public website. It uses relative URLs
for its identity, scope, icons, and shortcuts, so they resolve under the GitHub
Pages project path. It opens in the browser and does not promise offline access
or replace the self-hosted Prismedia app. Browser, Apple touch, and maskable icons
use the actual app artwork; see `static/img/icons/README.md`.

The 1200 × 630 social image is rendered from `branding/social-card.html`. Refresh
it after changing the homepage direction or product screenshots:

```bash
node documentation-site/scripts/render-social-card.mjs
```

As with the browser tests, `PLAYWRIGHT_CHANNEL=chrome` selects installed Chrome.
The metadata tests verify all sitemap pages, canonical URLs, distinct descriptions,
the 404 exclusion, icon dimensions, and manifest shortcut destinations.

GitHub Pages hosts this project beneath `/Prismedia/`. Crawlers read `robots.txt`
from the origin root; the project's copy cannot set rules for the whole host.
Google also selects its search favicon at the hostname level. A dedicated domain
would give the project control over those two surfaces. After deploying, submit
the published sitemap in Search Console and verify the indexed pages there.
Structured data describes the website and software without invented ratings or
reviews; it does not guarantee a particular search-result appearance.

## Feature documentation and video

Keep each feature demonstration paired with a task guide: requests, reading and listening,
native reader settings, or music playback. Describe prerequisites, the actual action, and how
to recognize the result. Distinguish web controls from native controls and saved progress from
live device handoff. Link guides from both the sidebar and the relevant library documentation.

For a published video, use a stable thumbnail and media URL with descriptive visible text.
Only add `VideoObject` metadata when the final video is available at those URLs, with its real
duration and publication date. A video should be the main content of a dedicated watch page
before targeting video search results; adding markup to an incidental homepage video does not
make it a watch page. Preserve captions or an equivalent text explanation for silent demos.

## Documentation search

The docs use [`@easyops-cn/docusaurus-search-local`](https://github.com/easyops-cn/docusaurus-search-local).
A production build generates a content-hashed local index of guide titles, headings,
and body text. Search runs in the browser without an external search service. The
search bar appears on documentation and search pages; the marketing header keeps
its existing navigation. Use **⌘K / Ctrl+K** to focus it.

Search requires a production build and preview (`pnpm docs:build`, then
`pnpm docs:serve`); the development server does not produce the complete index.
The results route is excluded from the sitemap and marked `noindex`; the guides
remain the canonical search-engine destinations. Browser tests exercise keyboard
navigation, mobile results, base-path links, and the generated index.

The pinned search package has a small pnpm patch to keep its full-results link
consistent with `trailingSlash: false`. Keep the browser assertion for the
`/search?q=…` destination when upgrading the package.
