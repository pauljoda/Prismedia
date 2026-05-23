---
sidebar_position: 7
title: Publishing
description: Package, version, and distribute your plugin.
---

# Publishing

This page covers what to do when your plugin works locally and you want other people to install it.

## Packaging

A plugin distribution is **a directory** with the manifest, code, and any vendored assets. The user installs the directory; Prismedia extracts it under `/data/plugins/<plugin-id>/` and registers it.

The expected layout for each runtime:

**TypeScript:**

```text
my-plugin/
├── manifest.yml
├── package.json
├── dist/
│   └── index.js          ← built; checked in or built by your release pipeline
└── README.md
```

You can include `src/` and `tsconfig.json` for transparency, but the runtime only loads `dist/`.

**Python:**

```text
my-plugin/
├── manifest.yml
├── main.py
├── lib/                  ← optional vendored deps
└── README.md
```

**Stash-compat:**

```text
my-plugin/
├── manifest.yml
└── definitions/
    └── SiteName.yml
```

Distribute as a tarball or a zip. The Plugins UI accepts both.

## What `plugin_packages` records

When the user installs your plugin, Prismedia inserts a row into `plugin_packages`:

| Column | Meaning |
| --- | --- |
| `plugin_id` | From `manifest.id`. Unique. |
| `version` | From `manifest.version`. Drives update detection. |
| `runtime` | From `manifest.runtime`. |
| `install_path` | Absolute path on disk: `/data/plugins/<plugin-id>/`. |
| `sha256` | Hash of the package archive at install time. Drives integrity checks. |
| `capabilities` | The map from the manifest, copied for fast filtering. |
| `manifest_raw` | The full manifest YAML, copied for forensic reading. |
| `enabled` | Defaults to `true` on install. |
| `source_index` | Where it came from: `prismedia-community`, `stash-community`, or `local`. |

You can browse the table via the [Settings](../users/settings.md#metadata-providers) page or query directly:

```sql
SELECT plugin_id, version, runtime, source_index, enabled, sha256
FROM plugin_packages
ORDER BY plugin_id;
```

## The community registry

[prismedia-community-plugins](https://github.com/pauljoda/prismedia-community-plugins) is the first-party plugin registry. The Plugins UI's **Prismedia Index** tab pulls from this repo so users can install with one click.

### Index format

The registry exposes an `index.json` listing every plugin with metadata and a download URL:

```json
{
  "plugins": [
    {
      "id": "tmdb",
      "name": "The Movie Database",
      "version": "0.3.1",
      "description": "Movie and TV series identification via TMDB API",
      "runtime": "typescript",
      "isNsfw": false,
      "tags": ["movies", "tv", "series"],
      "homepage": "https://www.themoviedb.org",
      "downloadUrl": "https://github.com/pauljoda/prismedia-community-plugins/releases/download/tmdb-0.3.1/tmdb-0.3.1.tar.gz",
      "sha256": "abc123def456..."
    },
    {
      "id": "tvdb",
      "name": "TheTVDB",
      "version": "0.2.0",
      "...": "..."
    }
  ]
}
```

Prismedia fetches the index, presents the list in the UI, and on **Install** downloads the `downloadUrl` archive, verifies the SHA-256, extracts under `/data/plugins/<id>/`, parses the manifest, and inserts the `plugin_packages` row.

### `source_index` values

| Value | Where the plugin came from |
| --- | --- |
| `prismedia-community` | Listed in prismedia-community-plugins. |
| `stash-community` | Mirrored from the Stash community scrapers repo. |
| `local` | Side-loaded by the user (not from a registry). |

Side-loaded plugins still appear in the **Installed** tab; they just don't get update notifications because there's no upstream to check.

## Submitting to the community registry

The registry is open. To add a plugin:

1. Build and test your plugin locally.
2. Tag a release in your plugin's git repo.
3. Open a PR to prismedia-community-plugins adding (or updating) an entry in `index.json`. Include the download URL, SemVer, and SHA-256.
4. CI in the registry repo verifies your manifest parses and the archive matches the SHA-256.
5. Once merged, your plugin appears in every user's **Prismedia Index** tab.

The registry README has the current PR template and validation rules; check there for specifics.

## Versioning

Plugins are SemVer-versioned. Update detection in the Plugins UI compares the installed `version` against the registry's latest. The user sees an **Update** button for any plugin with a higher SemVer in the index.

Bump rules:

| Bump | When |
| --- | --- |
| **PATCH** | Bug fixes, no API change. |
| **MINOR** | New capability declared, additional optional auth field, backwards-compatible behavior change. |
| **MAJOR** | Capability removed, auth field renamed, breaking output shape change. |

Pre-release suffixes (`0.4.0-beta.1`) work; the UI treats them as available updates from a pre-release tag if present.

## Auth migrations

If you rename an auth `key` between versions, existing user installs lose their credential — there's no auto-migration. Two practical options:

1. **Keep the old key around** as an additional optional auth field for one or two releases, with a note in the description that the user should migrate. Read either key in your code.
2. **Document it as a breaking change** and bump MAJOR. Users see "auth required" after the update.

Option 1 is friendlier; option 2 is honest.

## Update detection mechanics

The "Check for updates" flow:

1. UI fetches `index.json`.
2. For each installed plugin (`source_index = prismedia-community`), compare `version` against the index entry.
3. If newer, show **Update** in the row.

Click **Update** and Prismedia downloads the new archive, verifies SHA-256, extracts over the existing install path, re-reads the manifest, and updates `plugin_packages`. Existing `plugin_auth` rows are preserved.

## Side-loading

For private or in-development plugins:

1. Build your plugin directory (or archive).
2. From the Plugins UI, use **Install from file** to upload a `.tar.gz` or `.zip` of the plugin directory.
3. Or copy the directory into `/data/plugins/<plugin-id>/` directly and restart the app — the boot scan will pick it up.

Side-loaded plugins get `source_index = local` and don't receive update notifications. They run identically otherwise.

## What to put in your README

- **One-paragraph description.**
- **Capability list** — what actions your plugin implements.
- **Auth requirements** — link to where the user gets credentials.
- **Known limitations** — sites or content types that won't work.
- **License** — pick one explicitly.
- **Versioning policy** — SemVer is the default; spell it out if you do something else.

Plugins are software. Users will fork them, file issues, and depend on them. Treat the README accordingly.

## Reading the source

If you want the canonical answer to anything in this section, the registry-side code lives in:

- [`prismedia-community-plugins/`](https://github.com/pauljoda/prismedia-community-plugins) — the registry itself.
- `apps/backend` — Prismedia-side discovery, install, runtime dispatch, and auth resolution.
- `packages/plugins/src/manifest-parser.ts` — manifest parsing and validation.
