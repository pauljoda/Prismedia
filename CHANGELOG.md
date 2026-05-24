# Changelog

Important user-facing changes are documented here. This changelog is intentionally curated and high level; use the git history for commit-by-commit detail.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]
### What's New
- Fresh instances now serve generated thumbnails as soon as the API starts, so newly scanned media no longer shows broken cover links when the cache directory did not exist yet.
- Prismedia branding now uses the cleaned primary and NSFW logos throughout the app, with a larger sidebar brand lockup and a scalable vector line mark.
- Mobile install metadata now presents Prismedia with the correct app name, theme colors, and safer icon spacing for browser and home-screen surfaces.

### Added
- Added a centralized app settings registry and descriptor-driven settings UI for app-wide visibility, playback, subtitle, scan, generation, worker, and HLS defaults.
- Added a media wall toggle to entity grids for browsing thumbnail-only library layouts.

### Changed
- Updated README, docs, browser, and install branding surfaces to prefer the red accent logo while keeping the in-app logo mode-aware.
- Added a subtle sidebar logo glow so the transparent mark reads clearly against the dark rail.
- Updated app branding to use the cleaned normal/NSFW logo assets and promoted the mono mark to SVG for scalable uses.
- Updated web app manifest and mobile browser metadata for home-screen installation and browser UI theme colors.

### Fixed
- Restored richer running and queued job rows with job-kind grouping, target details, and live status messages.
- Fixed generated thumbnail and preview asset serving on fresh startup by creating and mounting the media cache directory before assets are requested.
- Fixed Prismedia logo PNG transparency so the app brand mark no longer carries a black square background.
- Reduced thumbnail grid scroll jank by deferring hover previews during scroll and avoiding unnecessary player/lightbox loading on non-lightbox library pages.
- Fixed video thumbnail grids so scan/probe metadata like duration, resolution, codec, bitrate, and container is shown on video cards.
- Fixed touch drag scrubbing for trickplay sprites and segmented thumbnail previews.

### Removed

### Docs
