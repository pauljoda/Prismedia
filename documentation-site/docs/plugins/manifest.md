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
  "manifestVersion": 2,
  "apiTags": ["prismedia"],
  "id": "openlibrary",
  "name": "Open Library",
  "version": "0.3.0",
  "runtime": "dotnet-process",
  "entry": "dist/Prismedia.Plugin.OpenLibrary.dll",
  "compat": {
    "pluginApiMin": "2.0.0",
    "pluginApiMax": null,
    "prismediaMin": "1.0.0",
    "prismediaMax": null
  },
  "auth": [],
  "isNsfw": false,
  "supports": [
    {
      "entityKind": "book",
      "actions": ["lookup-id", "lookup-url", "search"],
      "identityNamespaces": ["openlibrary", "openlibrarywork", "isbn"],
      "search": {
        "fields": [
          { "key": "title", "label": "Title", "type": "text", "required": true },
          { "key": "author", "label": "Author", "type": "text", "required": false },
          { "key": "year", "label": "First published", "type": "year", "required": false }
        ]
      }
    },
    {
      "entityKind": "person",
      "actions": ["lookup-id", "lookup-url", "search"],
      "identityNamespaces": ["openlibraryauthor"],
      "search": {
        "fields": [
          { "key": "title", "label": "Author", "type": "text", "required": true }
        ]
      }
    }
  ]
}
```

## Fields

| Field | Type | Notes |
| --- | --- | --- |
| `manifestVersion` | number | Manifest schema version. New plugins use `2`; version 1 is read only for compatibility. |
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

Each entry in `supports` is a complete routing declaration for one Prismedia
entity kind. It tells the core which actions the plugin can execute, which
persistent upstream identities it can resolve, and which fields its search UI
requires:

| Field | Type | Notes |
| --- | --- | --- |
| `entityKind` | string | Stable entity kind code, e.g. `book`, `person`, `video-series`. |
| `actions` | string[] | Action codes: `lookup-id`, `lookup-url`, and `search`. Structural children are returned in the proposal when Prismedia sets `includeStructuralChildren`; `cascade` is not an action. |
| `identityNamespaces` | string[] | Canonical lowercase external identity namespaces this kind can resolve, e.g. `tmdb`, `tmdbepisode`, or `openlibrarywork`. Required in manifest v2. |
| `search` | object? | Ordered plugin-owned search form. Required exactly when `actions` includes `search`. |

The identify pipeline only routes work to a plugin for kind/action pairs it
declares here, and the provider pickers in the UI filter on the same data.

### Plugin id versus external identity

The manifest `id` identifies the installed executable. An identity namespace
identifies an upstream record. They are deliberately independent: a plugin
whose id is `cinema-metadata` may resolve `{ "namespace": "tmdb", "value":
"83867" }`.

Identity namespaces are normalized lowercase. Identity values are opaque,
case-sensitive strings and may contain colons. Never lowercase, split, or
reinterpret a value in the core. If a structural child does not have a native
upstream id, the plugin must define a stable composite namespace/value that a
context-free `lookup-id` can resolve back to the same child.

The order of `identityNamespaces` is meaningful when a proposal carries more
than one accepted identity: Prismedia selects the first declared namespace
present on that proposal. Put the most specific round-trippable identity first.

### Search schema

`search.fields` is an ordered array. Prismedia renders it directly and sends
the submitted values in `IdentifyQuery.fields`; the core does not know keys
such as `seriesTitle`, `author`, or `album`.

| Field | Type | Notes |
| --- | --- | --- |
| `key` | string | Stable plugin-owned key. Unique within this kind's form. |
| `label` | string | Human-readable input label. |
| `type` | string | `text`, `number`, or `year`. |
| `required` | boolean | Whether a non-empty value is required. |
| `placeholder` | string? | Optional concise example. |
| `help` | string? | Optional explanatory copy. |

Plugins should read `query.fields` and continue accepting `query.title` as a
compatibility fallback. A field used only during search must not imply that its
value will still exist during a later identity-only review. Anything required
to rehydrate a selected result belongs in its persistent identity.

### Round-trip requirement

Every identity emitted by a search candidate or structural proposal must pass
the same test:

1. take the proposal's entity kind plus chosen namespace/value;
2. issue a context-free `lookup-id` to the same plugin;
3. receive the same entity kind and the exact case-sensitive identity.

This is what makes monitoring, request review, revision validation, and future
background enrichment independent of the original search session.

## Compatibility gating

At load time Prismedia checks `manifestVersion`, `apiTags`, and the `compat`
bounds against its own plugin-protocol and application versions. Artifacts that
fail any check are listed as incompatible instead of loading. Publishing details
live in [Publishing](./publishing.md).
