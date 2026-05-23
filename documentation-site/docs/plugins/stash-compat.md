---
sidebar_position: 6
title: Stash Compatibility
description: Wrap a Stash YAML scraper for use as an Prismedia plugin.
---

# Stash Compatibility

The Stash community has hundreds of YAML-defined site scrapers — battle-tested for the long tail of porn-site metadata. Prismedia runs them through a compatibility adapter so you don't have to rewrite them in TypeScript or Python.

## How it fits

```text
Stash YAML scraper definition
            │
            ▼
   stash-compat manifest.yml  ─┐
                               │
                               ▼
   packages/stash-compat/src/stash-adapter.ts
                               │
                               ▼
        Plugin execution envelope (same as native plugins)
                               │
                               ▼
              NormalizedVideoResult / NormalizedFolderResult / ...
```

The adapter:

- Maps Prismedia action names to Stash action names (`videoByURL` → `sceneByURL`, `videoByName` → `sceneByName`, `videoByFragment` → `sceneByFragment`, etc.).
- Runs the YAML scraper's HTTP / regex / xpath pipeline.
- Normalizes the output to Prismedia's shape.

## A stash-compat plugin

The plugin directory just contains a manifest pointing at the YAML definition:

```text
my-stash-plugin/
├── manifest.yml
└── definitions/
    └── ExampleSite.yml          ← the original Stash YAML
```

```yaml title="manifest.yml"
id: example-site
name: Example Site Scraper
version: 0.1.0
runtime: stash-compat
stashDefinition: definitions/ExampleSite.yml

isNsfw: true                       # most Stash scrapers are NSFW

capabilities:
  videoByURL: true
  videoByFragment: true
  performerByURL: true
  performerByName: true
```

You declare which Prismedia capabilities the wrapped scraper provides — capabilities the YAML doesn't actually implement will fail at execution time, so be honest.

## What the adapter handles

The adapter speaks the standard Stash scraper YAML dialect:

- **`sceneByURL`**, **`sceneByName`**, **`sceneByFragment`**, **`sceneByQueryFragment`**
- **`performerByURL`**, **`performerByName`**, **`performerByFragment`**
- **`movieByURL`**, **`movieByName`**, **`movieByFragment`**
- **`galleryByURL`**, **`galleryByName`**, **`galleryByFragment`**

Action mapping (Prismedia → Stash):

| Prismedia action | Stash action |
| --- | --- |
| `videoByURL` | `sceneByURL` |
| `videoByName` | `sceneByName` |
| `videoByFragment` | `sceneByFragment` |
| `performerByURL` | `performerByURL` |
| `performerByName` | `performerByName` |
| `performerByFragment` | `performerByFragment` |
| `movieByURL` | `movieByURL` |
| `movieByName` | `movieByName` |
| `galleryByURL` | `galleryByURL` |
| `galleryByName` | `galleryByName` |

Other Prismedia actions (`folderCascade`, `seriesCascade`, `audioByURL`, etc.) are not part of the Stash protocol; declaring them on a stash-compat plugin will fail at runtime.

## Auth and headers

Stash YAML supports HTTP headers and per-action driver options. The adapter wires these through transparently. If the scraper needs an API key or cookie, declare it as `auth` in the Prismedia manifest:

```yaml
auth:
  - key: SITE_API_KEY
    label: Site API Key
    required: true
    url: https://example.site/account/api

capabilities:
  videoByURL: true
```

In your YAML, reference the key as you would any Stash scraper variable. The adapter exposes auth values to the YAML driver context.

## What the adapter does NOT handle

Stash has features that don't translate cleanly:

- **JavaScript-driven scrapers** that depend on a Stash-specific JS runtime aren't supported. (Most YAML scrapers are pure HTTP + regex/xpath and work fine.)
- **Cascade actions** (`folderCascade`, `seriesCascade`) — the Stash protocol has no equivalent.
- **Per-result tag deduplication policies** specific to Stash — the Prismedia normalizer applies its own (case-insensitive for tags).
- **Stash UI integrations** (custom drawer behavior, in-Stash CSS) — Prismedia uses its own cascade drawer.

If your YAML scraper relies on any of these, it'll need a port to native TypeScript or Python.

## Result mapping

Stash result fields are normalized to Prismedia's shapes:

| Stash field | Prismedia field (NormalizedVideoResult) |
| --- | --- |
| `Title` | `title` |
| `Date` | `date` |
| `Details` | `details` |
| `URL` / `URLs` | `urls[]` |
| `Studio.Name` | `studioName` |
| `Performers[].Name` | `performerNames[]` |
| `Tags[].Name` | `tagNames[]` |
| `Image` | `imageUrl` |
| `Code` | `code` |
| `Director` | `director` |

For performers, studios, and tags the adapter only carries names through; nested rich data (performer aliases, performer ethnicity, etc.) is dropped by the simple `NormalizedVideoResult` shape. If you need the richer data, use the dedicated performer/studio actions which return more detail.

## Installing existing Stash scrapers

The Stash community publishes scrapers at [github.com/stashapp/CommunityScrapers](https://github.com/stashapp/CommunityScrapers). To use one:

1. Pick the YAML you want.
2. Build a small directory with a `manifest.yml` referencing it (as above) and the YAML file.
3. Install it from the Plugins UI just like any other plugin.

Or — for the long tail — install from the **Plugins → Stash Community** tab, which mirrors the upstream repo and auto-generates the wrapper manifest. One-click install, no manual packaging.

## Versioning

The Stash community doesn't strictly version each YAML scraper, so the version field on a stash-compat plugin tracks **your wrapper**, not upstream. When the upstream YAML changes, bump your wrapper version and republish so the registry's update detection fires.

## Debugging

When a stash-compat plugin returns nothing or a bad result, the adapter writes the raw HTTP response body to the worker logs. `docker compose logs prismedia | grep stash-adapter` is a good first look. Most failures are:

- The site changed its HTML and the regex no longer matches.
- The site requires a session cookie or bot-protection bypass.
- The endpoint moved.

YAML changes are easy: edit the file, restart the worker, retry the identify.

## When to drop down to native

Stash-compat is great for "this scraper exists, just use it." Native (TypeScript or Python) is the right move when:

- You need a richer result shape (cascade, candidate disambiguation, image candidates with metadata).
- You want batch support to deduplicate API calls.
- The provider has a real API (not screen-scraping) and you'd rather call JSON.
- You need behavior the YAML protocol doesn't express.

Most first-party Prismedia plugins (TMDB, TVDB, MusicBrainz, YouTube) are native because they call structured APIs and use cascade flows. Most long-tail provider integrations remain stash-compat because the YAML community has already done the work.
