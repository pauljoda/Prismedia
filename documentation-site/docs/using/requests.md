---
sidebar_position: 5
title: Requests
description: Discover media through metadata plugins, review the proposal, and let Prismedia acquire it.
---

# Requests

The **Request** page (under *Operate*) is Prismedia's front door for content that is not on disk yet.
It uses the same Entity and plugin proposal model as Identify: choose a source, search it, review the
metadata and structural children, then create the wanted Entity that the download will eventually fulfil.

![Request search](/img/screenshots/requests.png)

## Before you search

Requests need three pieces of configuration:

1. Install and enable a metadata plugin that declares Search and Lookup ID support for the kind you want.
2. Configure an acquisition profile and indexer/download-client settings under
   **Settings → Acquisition**.
3. Add an enabled library root that scans the matching medium: books, videos, or audio.

Plugins own their search schema. A TV source can ask for series title and year, while a book source can
ask for title and author. Prismedia renders those declared fields instead of forcing every medium through
one generic text query.

## Discover and review

In **Discover**:

1. Choose a content kind.
2. Choose one of the enabled plugins that supports that kind.
3. Fill in the plugin's search fields and run the search.
4. Open a result to review its canonical metadata proposal.

The review is the same proposal-oriented experience used by Identify. It shows the metadata, artwork,
relationships, and structural children the plugin supplied. Container results expose independently
requestable children, so you can choose seasons, books, or albums instead of accepting an opaque
all-or-nothing request.

Before committing, choose a compatible library root and acquisition profile. Container requests use the
same medium-neutral policies everywhere: **All current and future**, **Missing now**, **Future only**, or
**Manual selection**. The shared child picker applies them to seasons, books, albums, and future Entity
hierarchies; the selected policy also controls whether newly discovered direct children begin acquisition.

### NSFW visibility

Discover follows the app-wide NSFW preference. When NSFW content is hidden, Prismedia excludes plugins
marked NSFW and asks the selected provider to omit adult results.

## What happens after Request

Requesting content creates real fileless Entities immediately:

- A leaf such as a movie, book, episode, or album appears in its normal library grid as **Wanted**.
- A container such as a series, author, or artist is monitored, and the selected children are created
  beneath it using the same Entity hierarchy as on-disk content.
- Each acquisition keeps the target Entity ID, persistent plugin identity, profile, and import root.
- When import completes, Prismedia materializes the files onto that same Entity immediately; it does not
  wait for a later full-library scan to make the content ready.

The Entity's normal detail page is the management surface after commit. Its Acquisition section shows
monitoring, release search, active transfer/import state, retry controls, and file-management actions.

## Request workspace

The Request page also provides shared operational views:

- **Downloads** shows active transfers and imports.
- **Missing** shows monitored wanted content that still needs a release.
- **Cutoff unmet** shows owned content eligible for a quality upgrade.
- **History** shows durable acquisition events.

These views project the same acquisitions and Entities used by detail pages; there is no parallel
request-detail model or external request database.

## Removing or unmonitoring

Removing a wanted item tears down its in-flight acquisition before deleting the fileless Entity.
Unmonitoring is authoritative at any level of the Entity hierarchy: Prismedia stops and removes active
downloads, clears queued and pending/review/failed acquisition work, removes fileless descendants, and
clears Wanted state from the source-backed Entities it retains. Turning off one child also prevents an
All/Future parent from silently adding that child back. Verified on-disk files are removed only through an
explicit **Delete files** action.
