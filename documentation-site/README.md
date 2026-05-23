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

Deployment is handled by `.github/workflows/documentation-site.yml`.
