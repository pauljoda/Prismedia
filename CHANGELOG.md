# Changelog

Important user-facing changes are documented here. This changelog is intentionally curated and high level; use the git history for commit-by-commit detail.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]
### What's New
- Prismedia is now a complete self-hosted media library for videos, movies, shows, images, galleries, comics, ebooks, audio, people, studios, tags, collections, files, plugins, and background jobs.
- The app has been redesigned around the Prism Noir Luxe interface, with a cinematic dashboard, consistent detail pages, richer library grids, customizable navigation, and mobile-first browsing controls.
- Playback is much more capable: videos remember progress, direct-play or stream-copy when possible, transcode more efficiently when needed, expose clearer quality/status controls, and report consistent watched state to Jellyfin-compatible clients.
- Audio is now first-class, with album and artist libraries, a persistent player, queues, shuffle/repeat, waveform scrubbing, media-session controls, artwork, play counts, and restore-on-refresh behavior.
- Books and comics now include EPUB, PDF, CBZ, ZIP, and CBR support, built-in readers, reading progress, resume/start-over actions, grouped series/volumes/chapters, and OPDS catalog access for external reader apps.
- Identify has been rebuilt into a durable metadata workflow with provider search, review queues, cascade matching for related children, bulk accept, Auto Identify, and safer merge behavior that preserves existing metadata.
- Request is available under Operate, connecting to Radarr, Sonarr, and Lidarr for searching, filtering, submitting, updating, and tracking media requests with live service status.
- Library scanning is faster and smarter, with incremental rescans, batched persistence, prioritized thumbnail/metadata work, better folder organization, generated thumbnails, and safer cleanup of removed or disabled libraries.
- Jellyfin-compatible access is substantially broader, including profile-scoped SFW/NSFW visibility, API-key and session authentication, richer metadata, artwork, watched state, and compatibility fixes for clients such as Infuse, Swiftfin, Manet, Finamp, and Symfonium.
- SFW/NSFW controls now apply across browsing, plugins, identify, requests, OPDS, Jellyfin profiles, thumbnails, queues, and metadata so visibility stays consistent across the app and external clients.
- Release operations are now ready for channel publishing: the root package version is the source of truth, Docker builds validate the release metadata, pushes to `main` publish only the dev image, and alpha/beta/release images are published manually.

### Added
- Added OPDS 1.2 catalog support for external ebook and comic readers, including authenticated feeds, search, covers, downloads, profile visibility rules, and reverse-proxy-aware links.
- Added playback history and a Stats page for plays, skips, top items, recent activity, and timeframe-based summaries across video, books, and audio.
- Added Request workflows for Radarr, Sonarr, and Lidarr, including service setup, connection testing, defaults, TMDB/MusicBrainz enrichment, already-tracked updates, NSFW-aware search, and request history.
- Added persistent audio playback with a global player bar, mini-player, queue drawer, artist queues, media-session integration, waveform rendering, track ratings, and saved local playback state.
- Added EPUB and PDF scanning plus built-in readers with paged or continuous layouts, zoom, search, table of contents/outline support, links, downloads, and saved reading progress.
- Added configurable sidebar and mobile navigation sections, with rename, reorder, hide, collapse, reset, pinned mobile destinations, and server-saved layout preferences.
- Added dynamic and hybrid collections, including rule previews, media-specific filters, library-root filters, watched/progress filters, and collection cover modes.
- Added durable Identify queues, bulk identify, provider seeking, auto identify settings, cascade child matching, live apply progress, and review screens for movies, shows, seasons, episodes, books, music, galleries, people, studios, and tags.
- Added first-class editing for metadata, credits, roles, characters, studios, tags, links, dates, provider IDs, ratings, classification, poster/header artwork, and related entities across detail pages.
- Added operational controls for background jobs, worker status, transcode cache limits, file uploads from the Files tab, reversible scan exclusions, API access profiles, and centralized app settings.

### Changed
- Reworked the dashboard around Continue, Recent, recently added media, and a resume-focused hero so active items appear immediately instead of being buried in per-type shelves.
- Standardized detail pages and library grids on shared Prismedia building blocks, improving filters, sort, pagination, thumbnails, media-wall/feed views, responsive controls, and cross-page consistency.
- Made scans incremental, batched, deduplicated per media kind, and priority-aware so large unchanged libraries finish quickly while new items surface with useful metadata and covers first.
- Improved media organization rules for loose videos, movies, direct-child series, audio folders, ebook/PDF folders, galleries, single-image folders, comic archives, sidecars, and generated assets.
- Reworked video playback negotiation around client capabilities, direct play, direct stream, stream-copy, hardware-aware transcoding, clearer manual quality selection, and lower CPU usage.
- Rebuilt Identify around durable server jobs instead of fragile in-memory page state, with user-triggered searches taking priority over background auto-identify work.
- Expanded Jellyfin-compatible responses with richer Prismedia metadata, artwork, media sources, playback progress, collections, people filmographies, home shelves, and profile-scoped libraries.
- Tightened API and deployment behavior so generated API keys protect `/api/*`, the web app receives same-origin credentials automatically, and reverse proxies can bypass auth for client-compatible routes.
- Refreshed release workflows so validation, documentation publishing, and alpha/beta/release channel images are manual while `main` publishes only the dev image.
- Updated documentation, README screenshots, branding assets, install metadata, and app copy to match the current Prismedia v1 surface.

### Fixed
- Fixed many playback reliability issues, including first-play failures, slow or inaccurate seeking, stalled transcodes, remux segment errors, HDR/Dolby Vision handling, subtitle rendering, audio-track selection, progress bars, cache cleanup, and high CPU spikes.
- Fixed progress and history consistency so Prismedia and Jellyfin-compatible clients agree on resume points, completion, play counts, skips, watched/read state, and Start Over behavior.
- Fixed Identify so searches, provider retries, child cascades, metadata merges, artwork selection, NSFW propagation, duplicate provider data, music matching, and bulk accept behave reliably without losing existing metadata.
- Fixed scan and worker resilience, including startup ordering, stale running jobs, cancellation races, recurring scan timing, deleted/disabled libraries, missing cache assets, failed-job recovery, and Docker build stability.
- Fixed Jellyfin-compatible client setup, login, browsing, shelves, similar-item probes, collection roots, direct-child series, episode artwork, music sync, cast navigation, and playback metadata.
- Fixed OPDS edge cases for long client identifiers, reverse-proxied HTTPS links, folder-backed comics, series containers, covers, and acquisition feeds.
- Fixed Request service setup, saved defaults, redacted API keys, Arr lookup behavior, already-tracked updates, artist artwork fallbacks, and music search reliability.
- Fixed mobile and touch interactions across navigation, library menus, thumbnail scrubbing, lightboxes, readers, detail heroes, forms, buttons, flyouts, and narrow-screen wrapping.
- Fixed image, gallery, animated-image, feed, and lightbox behavior so inline clips, GIF/APNG files, portrait media, previews, preload, and aspect ratios render correctly.
- Fixed dynamic collection rules, previews, saved edits, smart collection queries, random sorting, page-level grids, tag/person/studio references, and orphan cleanup controls.

### Removed
- Removed Stash-compatible perceptual hashing (pHash). Fingerprint workflows now rely on MD5 and OpenSubtitles hash support, and the old pHash setting and StashBox pHash documentation are gone.
- Removed development-only route shells and redundant prerelease UI panels so the app surface and release notes focus on the production Prismedia experience.

### Docs
- Overhauled the README and documentation site around the current Prismedia app, including setup, browsing, scanning, playback, requests, identify, collections, settings, operations, plugins, and screenshots.
- Added Library & Scanning references with supported folder layouts and file extensions for videos, movies, series, images, galleries, comics, ebooks, and audio.
- Added Jellyfin Compatibility docs covering tested clients, sign-in profiles, API keys, SFW/NSFW visibility, setup flows, and reverse-proxy bypass rules for Authelia and Authentik.
- Added deployment and security docs for Docker, authentication, API keys, reverse proxies, release-channel images, and the .NET API serving the built Svelte app.
- Added developer-facing architecture and flow docs with diagrams for app boundaries, generated clients, jobs, identify, playback, and release-readiness review.
