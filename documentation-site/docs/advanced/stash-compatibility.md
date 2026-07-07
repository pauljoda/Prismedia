---
sidebar_position: 1
title: Stash Compatibility
description: Use Stash community scrapers as Prismedia identify providers.
---

# Stash Compatibility

The [Stash](https://stashapp.cc/) community maintains hundreds of YAML-defined site scrapers — battle-tested metadata sources for the long tail of sites that mainstream providers don't cover. Prismedia can **wrap those community scrapers** and run them as identify providers, so you don't have to rewrite them.

This is the extent of Prismedia's Stash interop today: Prismedia owns its own schema and identify model, and Stash scrapers feed into it like any other plugin. (Prismedia does not act as a Stash server, and StashBox fingerprint endpoints are not part of the current feature set.)

## What you get

- Browse and install scrapers from **Plugins → Stash Community**.
- Identify videos by **URL** or by **name**, and map scraped scenes — title, date, studio, performers, director, tags, and cover art — into Prismedia.
- Credited performers and the studio are proposed as reviewable cards, with their posters and bios pulled from the scraper's performer/studio lookups.

The wrapped scrapers appear in the same Identify provider picker as native plugins, and their results review and accept in the same cascade flow. See [Identify & Metadata](../using/identify.md).

## Installing community scrapers

1. Open **Plugins → Stash Community**.
2. Find the scraper for the site you want (the catalog mirrors the upstream community repo; Python-backed scrapers are included).
3. Install it. Prismedia wraps it with a plugin manifest and registers it.

You can also side-load a scraper you've packaged yourself — see [Stash Compatibility for plugin authors](../plugins/stash-compat.md) for the wrapper format and the action mapping (`videoByURL` → `sceneByURL`, and so on).

## NSFW handling

Stash scrapers are treated as **always NSFW**: every entity they create or touch is marked NSFW, and the scrapers themselves are hidden from the Plugins page, the Auto Identify picker, and the identify provider options while you browse in SFW mode. Reveal NSFW content (signed in as a user whose account [allows NSFW](../jellyfin/profiles.md)) to use them.

## Limitations

The wrapper runs the standard Stash YAML scraper pipeline (HTTP + regex/xpath, including the supported scene/performer/studio/gallery/movie actions). Stash features that depend on a Stash-specific JavaScript runtime, or Stash UI integrations, do not translate. Prismedia's own cascade actions (series/folder cascades) are not part of the Stash protocol. If a scraper needs those, it has to be ported to a native Prismedia plugin (see [Plugins](../plugins/overview.md)).

## See also

- [Identify & Metadata](../using/identify.md) — the review and accept flow.
- [Stash Compatibility (plugin authors)](../plugins/stash-compat.md) — wrapping a scraper yourself.
- [Plugin System Overview](../plugins/overview.md) — how providers fit together.
