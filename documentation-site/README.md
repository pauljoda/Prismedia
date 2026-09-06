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
mobile layout, folder-guide navigation, and the JavaScript-disabled fallback.
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

Deployment is handled by `.github/workflows/documentation-site.yml`.
