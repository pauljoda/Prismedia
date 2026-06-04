---
sidebar_position: 1
title: Plugin System Overview
description: What plugins are, the three runtimes, and how they fit into the identify engine.
---

# Plugin System Overview

Prismedia's metadata is **plugin-driven**. Posters, descriptions, people, studios, tags, episode breakdowns — none of it is hard-coded. Every provider is a plugin with a manifest, a declared capability set, and an execution envelope.

This page is the bird's-eye view: what kinds of plugins exist, how they relate, and what to read next.

## Three runtimes

| Runtime | What it is | When to use |
| --- | --- | --- |
| **TypeScript** | A compiled JS module loaded into the worker process via dynamic `import()`. | New providers when you want type safety, `npm` ecosystem, in-process speed. |
| **Python** | A standalone script invoked as a subprocess; talks JSON over stdin/stdout. | New providers where Python libraries make life easier (some scrapers, some data formats). |
| **Stash-compat** | A wrapper around a Stash YAML scraper. The community has hundreds of these; the adapter maps Stash actions onto Prismedia's envelope. | Pulling in existing Stash scrapers without rewriting them. |

All three speak the **same protocol** at the `executePlugin()` boundary:

```text
                     ┌──────────────────────┐
   identify request  │  PluginExecutionInput│
   ─────────────────►│  { action, input,    │
                     │    auth, batch }     │
                     └──────────┬───────────┘
                                │
                ┌───────────────┼───────────────┐
                ▼               ▼               ▼
         ┌────────────┐  ┌────────────┐  ┌────────────┐
         │ TypeScript │  │   Python   │  │   Stash    │
         │  loader    │  │ subprocess │  │  adapter   │
         └─────┬──────┘  └─────┬──────┘  └─────┬──────┘
               └────────┬──────┴───────┬──────┘
                        ▼              ▼
                ┌──────────────────────────────┐
                │  PluginExecutionOutput<T>    │
                │  { ok, result | results,     │
                │    error }                   │
                └──────────────┬───────────────┘
                               │
                               ▼
                  Normalizers → scrape_results row
```

Whether you write TypeScript or Python, your code answers a single question: *"given this input and these credentials, what metadata do you have?"*

## Wrapping Stash community scrapers

The Stash community's YAML site scrapers can be **wrapped** as Stash-compatible plugins and run through the same execution boundary as native ones. The adapter maps Prismedia actions onto Stash actions and normalizes the result. See [Stash Compatibility](../advanced/stash-compatibility.md) for the user-facing flow and [Stash Compatibility (plugin authors)](./stash-compat.md) for the wrapper format.

## What a plugin produces

A plugin returns a **normalized result** matched to the action it ran:

| Action category | Result type |
| --- | --- |
| `videoByURL`, `videoByName`, `videoByFragment` | `NormalizedVideoResult` |
| `folderByName`, `folderByFragment`, `folderCascade` | `NormalizedFolderResult` (with optional `episodeMap`) |
| `galleryByURL`, `galleryByFragment` | `NormalizedGalleryResult` |
| `imageByURL` | `NormalizedImageResult` |
| `audioByURL`, `audioByFragment`, `audioLibraryByName` | `NormalizedAudioTrackResult` / `NormalizedAudioLibraryResult` |
| `performer*` | Performer result (via the Stash adapter compatibility layer) |
| `movieByName`, `movieByURL`, `movieByFragment` | `NormalizedMovieResult` |
| `seriesByName`, `seriesByURL`, `seriesByFragment` | `NormalizedSeriesResult` (with optional disambig `candidates[]`) |
| `seriesCascade` | `NormalizedSeriesResult` with full `seasons[].episodes[]` tree |
| `episodeByName`, `episodeByFragment` | `NormalizedEpisodeResult` |

These shapes are documented in detail in [Capabilities](./capabilities.md).

The application normalizer trims strings, validates URLs, deduplicates names case-insensitively, coerces numbers, and accepts both singular and plural field names. Returning slightly-the-wrong shape isn't fatal — the normalizer is forgiving. Returning *nothing* useful is fine too: just `null`.

## What happens after a result lands

```text
plugin → normalized result
       → scrape_results row written (status = pending,
                                     proposed_* fields populated)
       → user reviews in the cascade drawer
       → on Accept: missing people/tags/studios created,
                    images downloaded to /data/cache/metadata/,
                    entity row updated, status = accepted
```

The cascade drawer is what turns one TMDB series result into a fully-populated series + N seasons + M episodes in your library. The plugin returns the tree; Prismedia walks it.

## First-party plugins

The CHANGELOG mentions **The Movie Database (TMDB)**, **TVDB**, **YouTube**, and **MusicBrainz**. They live in the [prismedia-community-plugins](https://github.com/pauljoda/prismedia-community-plugins) sister repo, not in the main repo. They're the easiest reference reads for "what does a real plugin look like."

You install them from **Plugins → Prismedia Index** in the web app. One click downloads, verifies, and registers them.

## Where plugin code lives

| Path | What it is |
| --- | --- |
| `packages/plugins/src/types.ts` | The wire-protocol types — `PrismediaPlugin`, `PluginExecutionInput`, `PluginExecutionOutput`, `PluginCapabilities`, every `Normalized*Result`. **The contract.** |
| `packages/plugins/src/manifest-parser.ts` | YAML manifest reader and validator. |
| `packages/plugins/src/ts-loader.ts` | TypeScript runtime loader. |
| `packages/plugins/src/executor.ts` | Python subprocess executor. |
| `packages/plugins/src/normalizer.ts` | Output normalizers. |
| `packages/stash-compat/src/stash-adapter.ts` | Stash-compat YAML adapter. |
| `apps/backend` | Prismedia-side glue: resolve manifests, credentials, execution, persistence, and accepted results. |

If you're going to read source, start with `packages/plugins/src/types.ts`. Everything else makes sense once you know the wire format.

## What to read next

- **Building one yourself**: [Manifest](./manifest.md) → [Capabilities](./capabilities.md) → [TypeScript Plugin](./typescript-plugin.md) or [Python Plugin](./python-plugin.md).
- **Bringing in a Stash YAML scraper**: [Stash Compatibility](./stash-compat.md).
- **Publishing for others**: [Publishing](./publishing.md).
