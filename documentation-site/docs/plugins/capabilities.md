---
sidebar_position: 3
title: Identify Protocol
description: The actions, request envelope, candidates, proposals, and identity rules shared by Prismedia plugins.
---

# Identify Protocol

Every native metadata plugin implements one protocol regardless of media kind.
Prismedia sends an `IdentifyPluginRequest` to the plugin process and receives an
`IdentifyPluginResponse` containing candidates, a hydrated proposal, or no
match.

The server-side source of truth is under
`apps/backend/src/Prismedia.Contracts/Plugins/`.

## Actions

| Action | Input | Expected result |
| --- | --- | --- |
| `search` | Plugin-defined `query.fields` plus a compatibility `query.title` | A candidates-only proposal containing `EntitySearchCandidate[]`. |
| `lookup-id` | One or more known `query.externalIds` | A hydrated `EntityMetadataProposal` for the exact identity. |
| `lookup-url` | `query.url` or known URL hints | A hydrated proposal resolved from that URL. |

Declare an action only when it works for that exact entity kind. Structural
hydration is not a fourth action: Prismedia sets `includeStructuralChildren`
on a normal lookup request.

## Request envelope

```ts
interface IdentifyPluginRequest {
  protocolVersion: number;
  action: 'search' | 'lookup-id' | 'lookup-url';
  auth: Record<string, string>;
  entity: IdentifyEntitySnapshot;
  query: IdentifyQuery;
  hints: IdentifyMatchHints;
  structuralContext?: IdentifyStructuralContext | null;
  includeNsfw: boolean;
  includeRelationshipDetails: boolean;
  includeStructuralChildren: boolean;
}

interface IdentifyQuery {
  title?: string | null;
  url?: string | null;
  externalIds?: Record<string, string> | null;
  requireChoice?: boolean | null;
  fields?: Record<string, string> | null;
}
```

`entity` is a minimal Prismedia Entity snapshot, not a provider-specific DTO.
`hints` carries identities and URLs already known by the Entity.
`structuralContext` carries ancestor snapshots and generic positions such as a
season or episode number when the request originates inside an existing Entity
tree.

Plugins must also support context-free `lookup-id` for every identity they
emit. Monitoring and reviewed requests intentionally cannot depend on the
original UI session or a parent Entity still being in memory.

## Search candidates

Search returns lightweight choices:

```ts
interface EntitySearchCandidate {
  externalIds: Record<string, string>;
  title: string;
  year?: number | null;
  overview?: string | null;
  posterUrl?: string | null;
  popularity?: number | null;
  candidateId?: string | null;
  source?: string | null;
  confidence?: number | null;
  matchReason?: string | null;
}
```

A candidate is not a Prismedia Entity. It is a provider result that carries
enough persistent identity for an exact lookup. Do not invent Entity ids or
capabilities for it.

## Hydrated proposals

```ts
interface EntityMetadataProposal {
  proposalId: string;
  provider: string;
  targetKind: ProposalKind;
  confidence?: number | null;
  matchReason?: string | null;
  patch: EntityMetadataPatch;
  images: ImageCandidate[];
  children: EntityMetadataProposal[];
  relationships: EntityMetadataProposal[];
  candidates: EntitySearchCandidate[];
  targetEntityId?: string | null;
}
```

The patch uses Prismedia-owned capability vocabulary: title, description,
external identities, URLs, tags, studio, credits, dates, stats, positions,
classification, and optional flags. Plugins do not return database rows or
third-party application schemas.

- `children` are structural: seasons, episodes, volumes, chapters, albums, or
  tracks.
- `relationships` are non-structural: people, studios, tags, and related works.
- `proposalId` is an opaque, case-sensitive selection handle. It must be unique
  within the proposal tree.
- `provider` is the plugin manifest id that produced the proposal.

## Identity contract

An external identity is `{ namespace, value }`:

- the namespace is canonical lowercase and declared by the manifest for the
  proposal's entity kind;
- the value is opaque, case-sensitive, and may contain colons;
- the plugin manifest id is not an identity namespace;
- every candidate and independently selectable structural child must carry a
  round-trippable identity;
- the exact plugin must resolve that kind/identity pair without falling through
  to another plugin that supports the same namespace.

When no upstream child id exists, define a plugin-owned composite namespace and
value, then parse it only inside the plugin. For example, an episode provider
might use `provider-episode: "series-id:season:1:episode:4"`. The core stores and
routes the value but never splits it.

## Review and commit

Identify and Discover use the same proposal UI:

1. search fields produce candidates;
2. choosing a candidate runs an exact `lookup-id`;
3. Prismedia shows the unflattened proposal;
4. Identify applies selected fields to an existing Entity, while Request
   materializes selected proposal nodes as Wanted Entities.

Request review computes a deterministic revision of the complete proposal.
Commit re-runs the exact plugin lookup without the proposal cache and rejects a
changed revision before writing anything. The client sends proposal ids, never
child identity values; the server derives identities again from the fresh
proposal.

## Validation checklist

Before publishing a plugin, test each supported kind:

- every declared action executes successfully;
- every required search field is enforced and every field reaches
  `query.fields`;
- `query.title` remains a compatibility fallback;
- every emitted candidate identity round-trips through context-free
  `lookup-id`;
- every selectable child identity round-trips to the same kind and exact value;
- `includeStructuralChildren: false` returns a fast root proposal;
- `includeStructuralChildren: true` returns truthful child structure;
- missing credentials and NSFW visibility are honored by the host;
- malformed identities return no match rather than resolving a different
  record.
