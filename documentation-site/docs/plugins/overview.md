---
sidebar_position: 1
title: Plugin System Overview
description: How native and Stash-compatible plugins fit into Entity identification, discovery, and monitoring.
---

# Plugin System Overview

Prismedia's metadata is **plugin-driven**. Posters, descriptions, people, studios, tags, episode breakdowns — none of it is hard-coded. Every provider is a plugin with a manifest, a declared capability set, and an execution envelope.

This page is the bird's-eye view: what kinds of plugins exist, how they relate, and what to read next.

## Runtime boundary

| Runtime | What it is | When to use |
| --- | --- | --- |
| **dotnet-process** | A short-lived .NET plugin executable that reads one JSON request and writes one JSON response. | Native Prismedia metadata providers. This is the first-party and community plugin contract. |
| **stash-compat** | A wrapper around a Stash YAML scraper. | Running existing Stash community scrapers through Prismedia's adapter. |

Native plugins speak the versioned `IdentifyPluginRequest` /
`IdentifyPluginResponse` protocol at a process boundary:

```text
                     ┌──────────────────────┐
   identify request  │ IdentifyPluginRequest│
   ─────────────────►│ { action, entity,    │
                     │   query, hints, ... }│
                     └──────────┬───────────┘
                                │
                ┌───────────────┴───────────────┐
                ▼                               ▼
         ┌────────────┐                  ┌────────────┐
         │ .NET child │                  │   Stash    │
         │  process   │                  │  adapter   │
         └─────┬──────┘                  └─────┬──────┘
               └───────────────┬───────────────┘
                               ▼
                ┌──────────────────────────────┐
                │ IdentifyPluginResponse       │
                │ { ok, result, error }        │
                └──────────────┬───────────────┘
                               │
                               ▼
                   candidate or proposal
```

The process is isolated from Prismedia persistence. It receives a minimal Entity
snapshot, plugin-owned query fields, known identities and structural context; it
returns candidates or a proposal. The core owns validation, persistence,
monitoring, acquisition, and metadata application.

## Wrapping Stash community scrapers

The Stash community's YAML site scrapers can be **wrapped** as Stash-compatible plugins and run through the same execution boundary as native ones. The adapter maps Prismedia actions onto Stash actions and normalizes the result. See [Stash Compatibility](../advanced/stash-compatibility.md) for the user-facing flow and [Stash Compatibility (plugin authors)](./stash-compat.md) for the wrapper format.

## What a plugin produces

A plugin returns one of three outcomes:

| Outcome | Contract | Meaning |
| --- | --- | --- |
| Candidate search | `EntitySearchCandidate[]` | Lightweight ambiguous matches for the user to choose from. |
| Hydrated match | `EntityMetadataProposal` | A complete metadata patch with artwork, structural children, and relationships. |
| No match / failure | `null` plus optional error | The provider could not answer this request. |

The action vocabulary is deliberately small: `search`, `lookup-id`, and
`lookup-url`. A plugin declares each supported kind/action pair, its persistent
identity namespaces, and its search form in manifest v2. See
[Manifest Reference](./manifest.md#entity-support).

`EntityMetadataProposal.children` contains structural children such as seasons,
episodes, volumes, chapters, albums, or tracks. `relationships` contains people,
studios, and tags. The request envelope's `includeStructuralChildren` and
`includeRelationshipDetails` flags let Prismedia choose a fast seed lookup or a
fully hydrated review.

## What happens after a result lands

```text
plugin → candidate search
       → user chooses one persistent identity
       → exact-plugin lookup-id
       → EntityMetadataProposal review
       → selected metadata is applied to an existing Entity
          or selected proposal nodes become Wanted Entities
```

Identify and Discover/Request share this proposal review model. The difference is
the destination: Identify applies capabilities to an Entity that already exists;
Request materializes selected proposal nodes as Wanted Entities, then hands them
to the acquisition policy for their kind.

Monitoring starts from the same persistent identity stored on the Entity. The
core asks the plugin registry which enabled plugin declares that kind, action,
and namespace; plugin installation ids are never assumed to equal upstream
identity namespaces.

Existing-Entity monitoring tools use that route too. A parent page sends the
local child Entity id; the server resolves its authoritative identity through
the plugin registry and keeps monitoring attached to that Entity even as
individual acquisition rows are created or removed. The same flow handles a
season, book, album, or future child kind. Opaque identity values remain
structured values throughout and may safely contain colons or mixed case.

Metadata plugins own upstream concerns: identity namespaces, search fields,
candidate ordering, exact-id lookup, and the metadata proposal Prismedia may
apply. Download-client calls and filesystem placement stay behind trusted
server-side acquisition/import modules. This keeps community metadata plugins
portable without granting them arbitrary access to library paths or transfer
credentials.

## First-party plugins

The first-party set includes **TMDB**, **AniList**, **YouTube**,
**MusicBrainz**, **MangaDex**, and **Open Library**. They live in the
[Prismedia-Plugins](https://github.com/pauljoda/Prismedia-Plugins) sister repo,
not in the main application repo. They are the best reference implementations
for the current protocol.

You install them from **Plugins → Prismedia Index** in the web app. One click downloads, verifies, and registers them.

## Where plugin code lives

| Path | What it is |
| --- | --- |
| `apps/backend/src/Prismedia.Contracts/Plugins/` | The wire-protocol contracts — `PluginManifest`, execution inputs/outputs, capability shapes. **The contract.** |
| `apps/backend/src/Prismedia.Infrastructure/Plugins/` | Manifest loading, the `dotnet-process` runner, credentials, execution, persistence, and accepted results. |
| `apps/backend/src/Prismedia.Infrastructure/StashCompat/` | The `stash-compat` runtime — runs standard Stash YAML scrapers natively. |

If you're going to read source, start with `Prismedia.Contracts/Plugins/PluginManifest.cs`. Everything else makes sense once you know the wire format.

## What to read next

- **Building one yourself**: [Manifest](./manifest.md) → [Identify Protocol](./capabilities.md), then read a published plugin in [Prismedia-Plugins](https://github.com/pauljoda/Prismedia-Plugins).
- **Bringing in a Stash YAML scraper**: [Stash Compatibility](./stash-compat.md).
- **Publishing for others**: [Publishing](./publishing.md).
