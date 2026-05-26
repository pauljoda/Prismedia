# Changelog

Important user-facing changes are documented here. This changelog is intentionally curated and high level; use the git history for commit-by-commit detail.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]
### What's New
- Fresh instances now serve generated thumbnails as soon as the API starts, so newly scanned media no longer shows broken cover links when the cache directory did not exist yet.
- Prismedia branding now uses the cleaned primary and NSFW logos throughout the app, with a larger sidebar brand lockup and a scalable vector line mark.
- Mobile install metadata now presents Prismedia with the correct app name, theme colors, and safer icon spacing for browser and home-screen surfaces.

### Added
- Added reversible file explorer exclusions so files and folders can be skipped by library scans without deleting them.
- Added a centralized app settings registry and descriptor-driven settings UI for app-wide visibility, playback, subtitle, scan, generation, worker, and HLS defaults.
- Added a durable Identify queue so search results and metadata proposals survive navigation and backend restarts.
- Added a media wall toggle to entity grids for browsing thumbnail-only library layouts.
- Added a worker status badge to Job Control so stalled queues show when the background worker is offline.

### Changed
- Image and audio scans now keep root-level loose files standalone while turning folders into nested galleries and audio libraries.
- Mobile entity grids now default unsaved thumbnail sizing to the largest card size while preserving saved user choices.
- Relationship people sections now use contextual labels such as People, Artists, and Performers outside video cast and crew pages.
- Detail pages now use the header breadcrumb trail for back navigation instead of page-local back links.
- Improved detail hero action buttons with stronger glass contrast and clearer active affordances.
- Updated detail edit forms so flags match the shared field label language and metadata examples stay entity-neutral.
- Bulk identify now runs as a durable background job instead of an ephemeral in-memory session, so progress survives app restarts and results feed directly into the identify review queue.
- Standardized all API error responses to use the `ApiProblem` format with consistent `code` and `message` fields.
- Removed development-only links from app navigation.
- Removed the summary stats cards from the Identify dashboard so the page starts with actionable queue content.
- Removed the redundant plugin inventory panel from the Identify dashboard.
- Simplified the Identify dashboard header by removing the redundant manual refresh action.
- Updated entity grid filter count badges to use Prism Noir Luxe brass-on-glass styling.
- Moved common thumbnail badges for NSFW, rating, and episode/season position onto the thumbnail image so below-title chips can focus on entity-specific metadata.
- Removed bitrate from entity detail metadata chips because it is better treated as playback/runtime information than stable descriptive metadata.
- Updated README, docs, browser, and install branding surfaces to prefer the red accent logo while keeping the in-app logo mode-aware.
- Added a subtle sidebar logo glow so the transparent mark reads clearly against the dark rail.
- Updated app branding to use the cleaned normal/NSFW logo assets and promoted the mono mark to SVG for scalable uses.
- Updated web app manifest and mobile browser metadata for home-screen installation and browser UI theme colors.

### Fixed
- Fixed empty entity detail tabs so pages without displayable detail content no longer show a tab strip or "No details available" placeholder.
- Fixed gallery detail hero thumbnails so container previews sample all child groups and reuse the shared thumbnail fallback in the hero backdrop.
- Fixed studio logo artwork so identified studio thumbnails appear on relationship cards and detail artwork surfaces.
- Fixed detail page poster previews so they use the same shared thumbnail hover behavior as entity grids.
- Fixed automatic scan scheduling so image, audio, and book scans update library scan timestamps and respect the configured interval.
- Brought the Jobs worker heartbeat badge into the Prism Noir design language with controlled corners, glass material, and status glow.
- Fixed animated image lightbox playback so video-backed items loop, keep progress visible at the bottom edge, and keep navigation controls out of the media frame.
- Fixed sub-gallery thumbnail navigation so gallery detail pages update immediately without a manual refresh.
- Removed duplicate shell padding from gallery detail pages.
- Fixed gallery and audio library browse pages so nested folders only appear inside their parent detail view.
- Fixed gallery scans so web-style animated media files such as MP4, WebM, and MOV are added as gallery image items.
- Fixed the Files context menu layering so actions stay visible over adjacent detail panes.
- Restored associated entity thumbnails in file details and placed the primary entity beside the properties card.
- Polished file detail properties so associated artwork and metadata share one cohesive card.
- Fixed Files NSFW filtering so folders represented only by NSFW catalog entities are hidden with their thumbnails.
- Fixed Files tree refresh so hidden folders and their already-loaded children are pruned from SFW mode.
- Made the Identify review queue respect NSFW mode, including filtered queue totals and NSFW markers on visible review rows.
- Clarified Identify review position updates so season and episode proposals show the sort order they will set instead of raw position keys.
- Renamed Identify review field diffs to Base fields and made review panels collapsible from their headers.
- Made Identify artwork review poster and backdrop groups larger now that logo candidates are no longer shown for most entities.
- Improved heavy Identify proposal pages by deferring offscreen review sections, showing async image skeletons, and using standard thumbnail cards for related entities.
- Improved Identify proposal review performance by rendering provider image previews lazily with smaller browser-cacheable artwork sizes.
- Fixed Identify dashboard navigation so returning to `/identify` from a review detail exits the stale detail view.
- Fixed Identify review navigation so walking between child proposals returns the page to the top of the new review.
- Fixed Identify relationship review so walked cast, studio, and tag proposals hydrate current metadata from existing related entities.
- Fixed Identify child proposal review so walked children load current metadata, use the same field/artwork/tag selectors as roots, and keep rich selector fields out of the diff table.
- Fixed Identify proposal review so structural children use selectable thumbnails for inclusion and final accept actions stay on the root review.
- Fixed Identify search result rows so each candidate groups its thumbnail and description in one selectable card.
- Fixed Identify search results so candidates show provider artwork and descriptions in list rows, and clicking a candidate selects it instead of navigating.
- Fixed Identify proposal review so current metadata, artwork selections, scoped credits, and tag toggles reflect what will actually be applied.
- Fixed Identify queueing so new items immediately attempt provider matching, fall back to title search on missed exact matches, and let proposal reviews return to candidate search without looping back to the same confident match.
- Fixed Identify NSFW provider imports so metadata proposals and accepted root, child, credit, studio, and tag entities inherit the provider NSFW flag.
- Fixed Identify accept so accepted child, credit, studio, and tag entities are marked organized with the root entity.
- Fixed Identify child proposal thumbnails so structural children show as matched existing children instead of New or Merge targets.
- Fixed Identify review thumbnails so relationship selectors show New or Merge status consistently.
- Simplified Identify child hydration so the app recursively calls the selected provider for existing children with parent context instead of accepting provider-created structural children.
- Fixed Identify relationship review so selectable thumbnail cards can disable cast, studio, and related entities before apply.
- Fixed Identify proposal review so nested children can be walked more than one level deep without losing parent context.
- Saved entity grid media-wall mode per grid, defaulted image/page grids to that view, and let book page cards show metadata again when media-wall mode is turned off.
- Kept the library toolbar reset action on the active filter row so the main toolbar buttons no longer shift when reset becomes available.
- Recovered stale running jobs from previous worker processes so the queue no longer appears stuck after a worker restart.
- Restored richer running and queued job rows with job-kind grouping, target details, and live status messages.
- Fixed generated thumbnail and preview asset serving on fresh startup by creating and mounting the media cache directory before assets are requested.
- Fixed Prismedia logo PNG transparency so the app brand mark no longer carries a black square background.
- Reduced thumbnail grid scroll jank by deferring hover previews during scroll and avoiding unnecessary player/lightbox loading on non-lightbox library pages.
- Fixed embedded episode grids so small season pages no longer use sticky grid chrome or viewport-edge thumbnail unloading.
- Kept lightweight grids lean by hiding pagination chrome below the paging threshold and making grid pagination non-sticky.
- Restored mobile thumbnail tap navigation while preserving horizontal drag scrubbing for previews.
- Fixed chapter comic reader close behavior so readers opened from resume/start-over links do not immediately reopen.
- Fixed comic reader flicker by moving book, volume, and chapter reading into a dedicated full-page reader route.
- Removed the comic reader mobile bottom bar and let the routed reader extend behind mobile browser toolbar space.
- Kept the comic reader toolbar hidden while touch-scrolling in webtoon mode so it only appears on intentional center taps.
- Kept entity thumbnail preview lists on user-controlled scrubbing instead of auto-cycling, while preserving first-tap navigation.
- Fixed long entity detail titles so filename-like text wraps instead of clipping on narrow screens.
- Fixed video thumbnail grids so scan/probe metadata like duration, resolution, codec, bitrate, and container is shown on video cards.
- Fixed touch drag scrubbing for trickplay sprites and segmented thumbnail previews.

### Removed

### Docs
