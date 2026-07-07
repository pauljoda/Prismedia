---
sidebar_position: 2
title: Manifest Reference
description: Every field in manifest.json — identity, runtime, compatibility, auth, and entity support.
---

# Manifest Reference

Every plugin artifact ships a `manifest.json` at its root. Prismedia reads it to
decide whether the plugin can load, which runtime executes it, and which entity
kinds and identify actions it serves.

- **Filename:** `manifest.json`
- **Format:** JSON
- **Contract:** `apps/backend/src/Prismedia.Contracts/Plugins/PluginManifest.cs`

## Example

```json
{
  "manifestVersion": 1,
  "apiTags": ["prismedia"],
  "id": "openlibrary",
  "name": "Open Library",
  "version": "0.3.0",
  "runtime": "dotnet-process",
  "entry": "dist/Prismedia.Plugin.OpenLibrary.dll",
  "compat": {
    "pluginApiMin": "1.0.0",
    "pluginApiMax": null,
    "prismediaMin": "1.0.0",
    "prismediaMax": null
  },
  "auth": [],
  "isNsfw": false,
  "supports": [
    { "entityKind": "book", "actions": ["lookup-id", "lookup-url", "search", "cascade"] },
    { "entityKind": "person", "actions": ["lookup-id", "lookup-url", "search"] }
  ]
}
```

## Fields

| Field | Type | Notes |
| --- | --- | --- |
| `manifestVersion` | number | Manifest schema version. Must be `1`. |
| `apiTags` | string[] | Generation tags; Prismedia ignores artifacts without the `prismedia` tag so older plugin systems can coexist in one index. |
| `id` | string | Stable provider/plugin code, e.g. `tmdb`. Unique across the registry. |
| `name` | string | Human-readable plugin name. |
| `version` | string | Artifact SemVer. |
| `runtime` | string | Runtime code — see [Runtimes](#runtimes). |
| `entry` | string | Entry artifact path, relative to the manifest directory when not rooted. |
| `compat.pluginApiMin` / `compat.pluginApiMax` | string / string? | Plugin-protocol version bounds. `null` max means "no upper bound". |
| `compat.prismediaMin` / `compat.prismediaMax` | string / string? | Prismedia application version bounds. |
| `auth` | object[] | Credential fields the plugin requests — see [Auth fields](#auth-fields). |
| `isNsfw` | boolean | Whether imported metadata should be marked NSFW by default. |
| `supports` | object[] | Entity kinds and identify actions the plugin serves — see [Entity support](#entity-support). |

## Runtimes

| Runtime | `entry` meaning | How it runs |
| --- | --- | --- |
| `dotnet-process` | Compiled plugin assembly, e.g. `dist/MyPlugin.dll`. | Executed by the .NET plugin process runner. |
| `stash-compat` | A standard Stash YAML scraper definition. | Executed natively by Prismedia's Stash-compat engine. You normally never write this manifest by hand — installing a scraper from the CommunityScrapers index synthesizes it (with a `stash-` id prefix). See [Stash Compatibility](./stash-compat.md). |

## Auth fields

Each entry in `auth` declares one credential the plugin wants:

| Field | Type | Notes |
| --- | --- | --- |
| `key` | string | Stable credential key passed to the plugin process. Unique within the array. |
| `label` | string | Label shown in plugin settings. |
| `required` | boolean | When `true`, identify actions are blocked until the credential is saved. |
| `url` | string? | Optional upstream page where users create or manage the credential. |

Users fill these in under **Plugins → Installed → credentials**; values are stored
server-side and injected at execution time.

## Entity support

Each entry in `supports` pairs a Prismedia entity kind code with the identify
actions the plugin implements for it:

| Field | Type | Notes |
| --- | --- | --- |
| `entityKind` | string | Stable entity kind code, e.g. `book`, `person`, `video-series`. |
| `actions` | string[] | Action codes: `lookup-id`, `lookup-url`, `search`, `cascade`. |

The identify pipeline only routes work to a plugin for kind/action pairs it
declares here, and the provider pickers in the UI filter on the same data.

## Compatibility gating

At load time Prismedia checks `manifestVersion`, `apiTags`, and the `compat`
bounds against its own plugin-protocol and application versions. Artifacts that
fail any check are listed as incompatible instead of loading. Publishing details
live in [Publishing](./publishing.md).
