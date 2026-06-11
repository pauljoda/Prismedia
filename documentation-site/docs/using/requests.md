---
sidebar_position: 5
title: Requests
description: Search Radarr, Sonarr, and Lidarr from Prismedia and request movies, series, artists, and albums.
---

# Requests

The **Request** page (under *Operate*) turns Prismedia into the front door for your download automation. Connect your Radarr, Sonarr, and Lidarr instances once, then search all of them from one place, see what you already have, and send requests — whole series, single seasons, an artist's catalog, or just one album — without opening the Arr UIs.

![Request search](/img/screenshots/requests.png)

## Connecting services

Services are configured in **Settings → Request Services**:

1. Pick the service type (Radarr, Sonarr, or Lidarr) and enter its URL and API key.
2. Run the **connection test** — it is required before saving, and on success it pulls the service's root folders, quality profiles, metadata profiles (Lidarr), and tags so you can choose defaults.
3. Pick defaults: root folder, quality profile, whether to search immediately after a request, and any Arr **tags** to stamp on everything Prismedia adds.
4. Radarr services also get a **minimum availability** setting (announced / in cinemas / released).

You can connect multiple instances of the same type (say, a 1080p and a 4K Radarr); one of each type is marked the default. The default is used when a request doesn't specify an instance, and every search fans out across all connected services in parallel — one slow or unreachable service shows a warning banner instead of failing the whole search.

## Searching

Type a query and Prismedia searches every connected service at once. Results come back as poster cards grouped by kind — movies, series, artists, albums — with year, certification, runtime or track count, and rating chips.

- **Filters** — narrow by kind (Movies / Series / Artists / Albums), by source service, and by availability (*Not in service* / *Already tracked*).
- **Sort** — relevance (provider order), year, rating, or title.
- **Already tracked** — anything that already exists in the target service carries a brass **In Radarr / In Sonarr / In Lidarr** badge, so you can tell at a glance what's new.
- **Sharable** — the search query and filters live in the URL, so the back button returns you to live results and a search can be linked or bookmarked.

### NSFW mode

Request search follows the app-wide NSFW toggle:

- **SFW (default)** — adults-only results (NC-17 / X style certifications) are filtered out. Regular mature ratings (R, TV-MA) remain visible.
- **NSFW** — movie searches additionally include TMDB's adult catalog, which the Arr metadata search hides even though Radarr can add those titles. Adult results carry an **XXX** chip and are requestable like anything else. This requires a configured TMDB provider (see [enrichment](#tmdb-enrichment) below).

Toggling the mode clears the grid and re-runs the search immediately.

## Detail pages

Opening a result shows a full detail page — backdrop hero, poster, overview, genres, studios, cast, and per-source ratings — using the same detail surface as your library.

![Request detail](/img/screenshots/request-detail.png)

What you can do depends on the kind:

| Kind | Detail page offers |
| --- | --- |
| **Movie** | Overview, cast, ratings, and a request panel with root folder / quality profile. |
| **Series** | Season checkboxes — request all of it or just the seasons you want. |
| **Artist** | The full discography (from MusicBrainz, newest first, with cover art) as a browsable, filterable grid. Requesting the artist adds their whole catalog. |
| **Album** | The track listing, and a request that adds **only that album** — the artist is created unmonitored so the rest of the catalog isn't fetched. |

Navigation is properly stacked: an album opened from a discography has a *Back to the artist* link, and the artist page links back to your search results.

### Already tracked → Update Request

When an item already exists in the service, the panel becomes **Update Request** instead of re-adding:

- Series show their **current season monitoring** preselected — check more seasons (or uncheck some) and submit to apply exactly what's shown.
- You can flip monitoring on/off and kick off a search for missing content.
- Quality profile and root folder are intentionally left alone — updates never touch the existing setup in the service.

### TMDB enrichment

The Arr services return fairly thin lookup data. When a **TMDB provider** is installed and configured in **Plugins** (with its API key), movie and series detail pages are hydrated directly from TMDB: cast with headshots, a wide backdrop, US certification, and ratings from multiple sources (TMDB, IMDb, Rotten Tomatoes, Metacritic). Enrichment is advisory — if TMDB is unreachable or unconfigured, the page simply shows what the Arr service provided.

Music details are enriched the same way from **MusicBrainz** (no key needed): artist discographies and album track listings come straight from there, since Lidarr can't list albums for artists it doesn't have yet.

## Request history

The **History** button on the Request page opens a log of everything you've submitted: what, when, and through which service. Each entry's status is refreshed **live from the service** when you open the page:

| Status | Meaning |
| --- | --- |
| **Submitted** | Accepted by the service; no further status observed yet. |
| **Pending** | Tracked and monitored, nothing downloaded yet. |
| **Downloading** | A related download is active in the service's queue. |
| **Partial** | Some, but not all, of the requested content is downloaded. |
| **Available** | Everything requested has been downloaded. |
| **Removed** | The item is no longer in the service's library. |
| **Unknown** | The service couldn't be reached; the last known status is shown. |

Entries can be removed individually, and history survives even if the service it came from is later deleted.

## Notes

- Requests talk to your Arr services with the API keys you configured; Prismedia itself never downloads anything — the Arr services do the searching and grabbing as usual.
- A request does not import anything into Prismedia by itself. Once the Arr service has downloaded into a folder Prismedia watches, a normal [library scan](../library/overview.md) picks it up.
