---
sidebar_position: 2
title: Manifest Reference
description: Every field in manifest.yml — required, optional, and runtime-specific.
---

# Manifest Reference

Every plugin lives in its own directory and ships with a `manifest.yml` at the root. This page is the complete reference.

## File location and format

- **Filename:** `manifest.yml`
- **Format:** YAML
- **Parser:** `packages/plugins/src/manifest-parser.ts:readManifest()` (validation in `validateManifest()`)

The manifest is read every time the plugin is invoked, so changes don't require a process restart.

## Required fields

```yaml
id: tmdb                  # unique plugin identifier (kebab-case)
name: The Movie Database  # display name
version: 0.3.1            # SemVer
runtime: typescript       # "typescript" | "python" | "stash-compat"
```

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `id` | string | yes | Unique across the registry. Kebab-case recommended. |
| `name` | string | yes | Shown in the Plugins UI and the provider picker. |
| `version` | string | yes | SemVer. Drives the "update available" detection. |
| `runtime` | enum | yes | Selects the execution path — see runtime-specific fields below. |

## Optional fields (all runtimes)

```yaml
author: "Prismedia Community"
description: "Movie and TV series identification via TMDB API"
homepage: "https://www.themoviedb.org"
isNsfw: false
tags: [movies, tv, series]
auth:
  - key: TMDB_API_KEY
    label: "TMDB API Key (v3 auth)"
    required: true
    url: "https://www.themoviedb.org/settings/api"
capabilities:
  videoByName: true
  videoByURL: true
  folderByName: true
  folderCascade: true
```

| Field | Type | Notes |
| --- | --- | --- |
| `author` | string | Displayed in the Plugins UI. |
| `description` | string | Displayed in the Plugins UI. Plain text. |
| `homepage` | URL | Linked in the Plugins UI. |
| `isNsfw` | boolean | Defaults to `false`. When `true`, the plugin only appears in the provider picker when NSFW mode is **Show**. |
| `tags` | string[] | Free-form. Used for filtering in the Plugins UI. |
| `auth` | `PluginAuthField[]` | Credential fields. See below. |
| `capabilities` | `PluginCapabilities` | Boolean map of supported actions. See [Capabilities](./capabilities.md). |

## Runtime-specific fields

Fields that only apply to one runtime:

| Field | Runtime | Meaning |
| --- | --- | --- |
| `entry` | typescript | Relative path to the compiled JavaScript entry, e.g. `dist/index.js`. |
| `script` | python | Command + args, e.g. `["python3", "main.py"]`. The first element is the executable; the rest are passed as args. |
| `requires` | python | Sibling package directories to expose via `PYTHONPATH` (e.g. shared helper packages distributed alongside this plugin). |
| `stashDefinition` | stash-compat | Relative path to the Stash YAML scraper definition, e.g. `definitions/site.yml`. |

**TypeScript example:**

```yaml
runtime: typescript
entry: dist/tmdb.js
```

**Python example:**

```yaml
runtime: python
script: ["python3", "main.py"]
requires: ["lib/musicbrainz-helper"]
```

**Stash-compat example:**

```yaml
runtime: stash-compat
stashDefinition: definitions/SomeSite.yml
```

## Auth field schema

Each entry in `auth` describes one credential the user has to provide before the plugin can run.

```ts
interface PluginAuthField {
  key: string;       // stable key — passed to plugin as auth[key]
  label: string;     // UI label in Settings → Plugins
  required: boolean; // default: true
  url?: string;      // optional link to where the user gets the credential
}
```

YAML example:

```yaml
auth:
  - key: TMDB_API_KEY
    label: "TMDB API Key (v3 auth)"
    required: true
    url: "https://www.themoviedb.org/settings/api"

  - key: STUDIO_OVERRIDE
    label: "Force studio name (optional)"
    required: false
```

The values the user sets are stored encrypted in `plugin_auth.encrypted_value` and decrypted in-memory only at the moment of plugin execution. The `PRISMEDIA_SECRET` env var is the encryption key — set it in production so credentials survive container recreations.

## A complete real example: TMDB

```yaml
id: tmdb
name: The Movie Database
version: "0.3.1"
author: "Prismedia Community"
description: "Movie and TV series identification via TMDB API"
homepage: "https://www.themoviedb.org"
isNsfw: false
tags: [movies, tv, series]

runtime: typescript
entry: "dist/tmdb.js"

auth:
  - key: TMDB_API_KEY
    label: "TMDB API Key (v3 auth)"
    required: true
    url: "https://www.themoviedb.org/settings/api"

capabilities:
  videoByURL: true
  videoByName: true
  folderByName: true
  folderCascade: true
  supportsBatch: false
```

## Validation rules

`validateManifest()` enforces:

- **`id`, `name`, `version`, `runtime`** must be non-empty strings.
- **`runtime`** must be one of `"typescript"`, `"python"`, `"stash-compat"`.
- **`entry`** is required when `runtime` is `"typescript"`.
- **`script`** must be a non-empty array when `runtime` is `"python"`.
- **`stashDefinition`** is required when `runtime` is `"stash-compat"`.
- **Auth field `key`** values must be unique within the array.
- **`capabilities`** must be an object; unknown keys are ignored, but typos are silent — declare carefully.

A bad manifest causes `installPlugin()` to fail with a descriptive error; the user sees the error in the Plugins UI when they try to install.

## How the manifest is consumed

At install time:

1. The Plugins UI downloads the package.
2. The web app extracts it under `/data/plugins/<plugin-id>/`.
3. Reads + validates `manifest.yml`.
4. Computes the package SHA-256.
5. Inserts a row into `plugin_packages` with `manifest_raw`, `capabilities`, `runtime`, `install_path`, `sha256`, `enabled = true`.

At execution time:

1. `executePlugin()` looks up the plugin row.
2. Reads `manifest.yml` from `install_path`.
3. Resolves auth from `plugin_auth` and decrypts.
4. Dispatches into the runtime (TypeScript loader, Python subprocess, or Stash adapter) with `{ action, input, auth }`.
5. Normalizes the result.

If the manifest changes on disk after install (rare; usually you're upgrading via the UI), the new `capabilities` and `runtime` will be re-read on next install.

## Tips

- **Pick the smallest capability set you can support.** A plugin that declares it can do `videoByName` but returns `null` half the time is worse than one that doesn't declare it at all — declared capabilities feed the provider picker.
- **Declare `isNsfw` honestly.** Mis-declared providers leak into SFW mode pickers.
- **Use `tags` to help discovery.** The Plugins UI filters by tags.
- **Keep `version` SemVer.** Update detection and version-pinning rely on it.
