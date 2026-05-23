---
sidebar_position: 4
title: TypeScript Plugin
description: Build a native TypeScript plugin from scratch.
---

# TypeScript Plugin

TypeScript plugins run in-process inside the worker. Lowest overhead, full type-safety, and access to the npm ecosystem.

## What you ship

A directory with this layout:

```text
my-plugin/
├── manifest.yml
├── package.json
├── tsconfig.json
├── src/
│   └── index.ts
└── dist/
    └── index.js          ← what manifest.entry points at
```

The user installs the directory; Prismedia reads `manifest.yml`, dynamically imports `dist/index.js`, and calls `plugin.execute(action, input, auth)` on each request.

## The interface

Your default export must satisfy `PrismediaPlugin` from `packages/plugins/src/types.ts:178`:

```ts
export interface PrismediaPlugin {
  capabilities: PluginCapabilities;
  execute(
    action: string,
    input: PluginInput,
    auth: Record<string, string>,
  ): Promise<unknown>;
  executeBatch?(
    action: string,
    items: BatchItem[],
    auth: Record<string, string>,
  ): Promise<Array<{ id: string; result: unknown | null }>>;
}
```

`execute()` is required; `executeBatch()` is optional and only used when `capabilities.supportsBatch === true`.

The return value of `execute()`:

- Returning a normalized result object → handled as `{ ok: true, result }`.
- Returning `null` → handled as "no match"; the user sees no candidate for this row.
- Throwing an `Error` → caught and turned into `{ ok: false, error: <message> }`.

You can also explicitly return `{ ok: false, error: "..." }` and it'll be propagated as-is.

## A complete minimal plugin

```ts title="src/index.ts"
import type {
  PrismediaPlugin,
  PluginInput,
  NormalizedMovieResult,
} from '@prismedia/plugins';

interface Auth {
  TMDB_API_KEY: string;
}

const TMDB_BASE = 'https://api.themoviedb.org/3';

async function searchMovie(title: string, key: string) {
  const params = new URLSearchParams({ api_key: key, query: title });
  const res = await fetch(`${TMDB_BASE}/search/movie?${params}`);
  if (!res.ok) throw new Error(`TMDB search failed: ${res.status}`);
  const json = (await res.json()) as { results: Array<{ id: number; title: string; release_date?: string }> };
  return json.results[0] ?? null;
}

async function fetchMovieDetail(id: number, key: string) {
  const params = new URLSearchParams({
    api_key: key,
    append_to_response: 'credits,images',
  });
  const res = await fetch(`${TMDB_BASE}/movie/${id}?${params}`);
  if (!res.ok) throw new Error(`TMDB detail failed: ${res.status}`);
  return res.json();
}

async function handleMovieByName(
  input: PluginInput,
  auth: Record<string, string>,
): Promise<NormalizedMovieResult | null> {
  const key = auth.TMDB_API_KEY;
  if (!key) throw new Error('TMDB_API_KEY not configured');

  const title = input.title ?? input.name;
  if (!title) return null;

  const hit = await searchMovie(title, key);
  if (!hit) return null;

  const detail = await fetchMovieDetail(hit.id, key);

  return {
    title: detail.title,
    originalTitle: detail.original_title,
    overview: detail.overview,
    releaseDate: detail.release_date,
    runtime: detail.runtime,
    genres: (detail.genres ?? []).map((g: { name: string }) => g.name),
    studioName: detail.production_companies?.[0]?.name,
    cast: (detail.credits?.cast ?? []).slice(0, 20).map((c: any) => ({
      name: c.name,
      character: c.character,
      order: c.order,
    })),
    posterCandidates: (detail.images?.posters ?? []).map((p: any) => ({
      url: `https://image.tmdb.org/t/p/original${p.file_path}`,
      language: p.iso_639_1 ?? undefined,
      width: p.width,
      height: p.height,
      aspectRatio: p.aspect_ratio,
    })),
    backdropCandidates: (detail.images?.backdrops ?? []).map((b: any) => ({
      url: `https://image.tmdb.org/t/p/original${b.file_path}`,
      width: b.width,
      height: b.height,
      aspectRatio: b.aspect_ratio,
    })),
    logoCandidates: [],
    externalIds: { tmdb: String(detail.id) },
    rating: detail.vote_average,
  };
}

const plugin: PrismediaPlugin = {
  capabilities: {
    movieByName: true,
  },
  async execute(action, input, auth) {
    switch (action) {
      case 'movieByName':
        return handleMovieByName(input, auth);
      default:
        return null;
    }
  },
};

export default plugin;
```

## Package layout

```json title="package.json"
{
  "name": "prismedia-tmdb-plugin",
  "version": "0.3.1",
  "type": "module",
  "main": "dist/index.js",
  "scripts": {
    "build": "tsc",
    "watch": "tsc -w"
  },
  "devDependencies": {
    "@prismedia/plugins": "*",
    "typescript": "^5"
  }
}
```

```json title="tsconfig.json"
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ES2022",
    "moduleResolution": "Bundler",
    "outDir": "dist",
    "declaration": false,
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true
  },
  "include": ["src/**/*"]
}
```

```yaml title="manifest.yml"
id: tmdb-mine
name: My TMDB Plugin
version: 0.3.1
runtime: typescript
entry: dist/index.js

auth:
  - key: TMDB_API_KEY
    label: TMDB API Key (v3)
    required: true
    url: https://www.themoviedb.org/settings/api

capabilities:
  movieByName: true
```

Build with `pnpm build` (or `npm run build`); the loader picks up `dist/index.js`.

## CommonJS or ESM

The loader accepts both. It detects CommonJS by sniffing `exports.` or `module.exports` in the entry file and writes a sentinel `package.json` (`"type": "commonjs"`) next to it to disable ESM parsing. ESM works without sentinel.

If you compile to CommonJS, your tsconfig:

```json
{
  "compilerOptions": {
    "module": "CommonJS",
    "target": "ES2022",
    "outDir": "dist"
  }
}
```

## Batch support

If your provider has bulk endpoints, implement `executeBatch` and set `capabilities.supportsBatch: true`. The engine will deliver up to ~50 items per call (the exact limit depends on the calling site) and you correlate by `id`:

```ts
async executeBatch(action, items, auth) {
  // ... fetch a batch from the provider ...
  return items.map((item) => ({
    id: item.id,
    result: lookup(item.input) ?? null,
  }));
}
```

## Errors

Three patterns:

```ts
// 1. Provider returned nothing useful
return null;

// 2. Provider had an error you want to surface
return { ok: false, error: 'TMDB rate-limited; try again later' };

// 3. Throw — caught and converted automatically
throw new Error('TMDB_API_KEY not configured');
```

All three end up in the cascade drawer or the per-row identify state.

## Logging

Use `console.log` / `console.error`. Output goes to the worker's stderr/stdout and shows up in `docker compose logs prismedia`. Don't log secrets — `auth` values appear in your handler.

## Local development

The loader reads from `install_path` (set when the plugin is installed). The fastest dev loop is to:

1. Install your plugin via the UI once. Note its install path: `/data/plugins/<id>/`.
2. Bind-mount your dev directory over that path, or symlink:
   ```bash
   docker compose exec prismedia ln -snf /workspace/my-plugin /data/plugins/my-plugin
   ```
3. Run `tsc -w` in your plugin directory to keep `dist/` fresh.
4. Re-run identify; the loader picks up the new `dist/index.js` because the manifest is re-read on each invocation.

(Note: TypeScript modules are cached by `import()` — Node won't re-import a changed `dist/index.js` without a worker restart. Restart the worker after a rebuild for changes to take effect.)

## Security notes

- TypeScript plugins run **in-process** inside the worker. They have full Node.js capabilities. Only install plugins you trust.
- The plugin sees `auth` values as plain text — the encryption layer is at the database boundary, not the plugin boundary.
- File-system access from a plugin is unrestricted; if you only need network, only do network.
