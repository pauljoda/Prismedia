---
sidebar_position: 5
title: HLS Streaming
description: How videos stream through the .NET playback pipeline.
---

# HLS Streaming

Prismedia serves video playback through the .NET backend. The Svelte player
posts the browser's playback profile to `/api/playback/videos/{entityId}/plan`.
The returned Prismedia playback plan selects a direct source, stream-copy path,
or adaptive HLS transcode and supplies the corresponding authenticated URLs.

Native playback routes include:

- `/api/playback/videos/{entityId}/stream` for the original range-enabled source
- `/api/playback/videos/{entityId}/hls/{asset}` for generated playlists and segments
- `/api/playback/videos/{entityId}/trickplay/{width}/...` for scrubber tiles
- `/api/playback/sessions/*` for start, progress, ping, and stop observations

Bearer tokens are preferred. A playback URL may carry `access_token` when the
media element cannot attach an authorization header.

## Ownership

- Playback negotiation lives in `apps/backend/src/Prismedia.Infrastructure/Videos`.
- Public playback routes live in `apps/backend/src/Prismedia.Api/Endpoints`.
- The Svelte player adapts API responses in `apps/web-svelte/src/lib/entities/video-capabilities.ts`.
- Player load/reload behavior lives in `apps/web-svelte/src/lib/player/video-player-load.ts`.

Do not add SvelteKit streaming routes or TypeScript HLS builders.
