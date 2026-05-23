---
sidebar_position: 2
title: StashBox Endpoints
description: Configure fingerprint-based identification against StashBox-protocol servers.
---

# StashBox Endpoints

StashBox is a community-run identification service: you send a fingerprint (perceptual hash, OpenSubtitles hash, or MD5), it tells you what the content is. Multiple StashBox servers exist (`stashdb.org`, `theporndb.net`, others) and they speak the same GraphQL protocol.

This page covers configuring endpoints, identifying against them, and contributing fingerprints back.

## What StashBox is (and isn't)

| StashBox is | StashBox is not |
| --- | --- |
| A GraphQL service indexed by content fingerprint | A plugin |
| A community-curated metadata source | Owned by Prismedia |
| Where fingerprint identification happens | A scraper that takes URLs |

StashBox endpoints live in the `stashbox_endpoints` table and are queried by the client at `packages/stash-compat/src/stashbox/client.ts`. They are not in `plugin_packages`. They appear in the same Identify provider pickers as native plugins because the cascade UI is provider-agnostic.

## Adding an endpoint

Open **Plugins → StashBox** in the web app.

Click **Add endpoint** and fill in:

| Field | Meaning |
| --- | --- |
| **Name** | Friendly label shown in the provider picker. |
| **Endpoint URL** | The GraphQL endpoint. Examples: `https://stashdb.org/graphql`, `https://theporndb.net/graphql`. |
| **API key** | Your account's API key. Get it from the endpoint's web UI; instructions vary per endpoint. |

Click **Test** — Prismedia issues a probe query to verify the URL + key. If it returns OK, the endpoint is healthy. Hit **Save**.

The endpoint is stored in `stashbox_endpoints` (encrypted API key) and immediately appears in the provider picker on the **Videos** tab of the Identify hub.

## Identify by fingerprint

When you run an identify on a video and the StashBox endpoint is selected as provider:

1. The engine collects every fingerprint Prismedia has for the video — `md5`, `oshash`, `phash` (whichever are populated).
2. It calls the StashBox `findScene` query for each.
3. Returned scenes are converted to `NormalizedVideoResult` shapes and presented as candidates.
4. You accept or reject as usual.

The matching engine uses **all available algorithms**. A video with both an oshash and a phash is more likely to match than one with only an oshash.

If `phash` is `NULL` because pHash generation was skipped, run the **Backfill pHashes** diagnostic in **Settings → Generated Storage** first.

## Contributing fingerprints back

Accepting a StashBox-origin match links your local video to the remote scene (`stash_ids` row) **and** queues a fingerprint submission. Each algorithm Prismedia has for that video is submitted to the endpoint, helping the next person identify the same content.

The submission flow:

```text
Accept (StashBox match)  →  Auto-link (stash_ids row)
                          →  Queue submitJob per algorithm
                          →  StashBox GraphQL submission
                          →  fingerprint_submissions row (success | error)
```

You can see the trail in the database:

```sql
SELECT entity_type, algorithm, status, error, submitted_at
FROM fingerprint_submissions
ORDER BY submitted_at DESC LIMIT 20;
```

See [pHash Contribution](./phash-contribution.md) for the deeper walk-through, including how the pHash itself is computed Stash-compatibly.

## NSFW and SFW endpoints

Most StashBox endpoints are **NSFW-by-default** because they're built around adult-content metadata. The provider picker filters them out when NSFW mode is **Off** — set NSFW to **Show** (or **LAN auto-enable** when on the LAN) to see them.

Endpoints that are explicitly SFW can be configured the same way but won't be hidden in **Off** mode.

## Multiple endpoints

You can configure as many endpoints as you want. Each appears as a separate provider in the picker — pick one per identify run, not multiple at once. Run the same identify against a different endpoint if the first didn't find anything.

## API key safety

API keys are stored encrypted in `stashbox_endpoints.api_key` using the same `PRISMEDIA_SECRET` env var that protects plugin auth. **Set `PRISMEDIA_SECRET` in production** so keys survive container recreations:

```yaml
services:
  prismedia:
    image: ghcr.io/pauljoda/prismedia:latest
    environment:
      PRISMEDIA_SECRET: <a-long-random-string>
```

If `PRISMEDIA_SECRET` changes between restarts, encrypted values become unreadable and you'll have to re-enter every API key.

## Looking up performers

The StashBox protocol also supports performer lookup. The dedicated route `/api/stashbox-endpoints/[id]/lookup/performer` is used by the **Performers identify** flow when StashBox is selected as provider. The cascade drawer renders performer candidates the same way it renders video candidates.

## Disabling without deleting

Toggle **Enabled** off on an endpoint to remove it from the provider picker without losing the configured URL + API key. Useful when an endpoint is down or you want to A/B between endpoints.

## Reading the source

- `packages/stash-compat/src/stashbox/client.ts` — GraphQL client.
- `apps/backend` — endpoint config, persistence, identify, lookup, submit, and result-accept flows.
- `packages/plugins` and `packages/stash-compat` — plugin and StashBox protocol helpers.
