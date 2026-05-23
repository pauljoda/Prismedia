---
sidebar_position: 5
title: HLS Streaming
description: How videos stream through the .NET playback pipeline.
---

# HLS Streaming

Prismedia serves video playback through the .NET backend. The Svelte player
negotiates playback through Jellyfin-compatible endpoints and then consumes
direct streams, adaptive HLS playlists, HLS segments, subtitle assets, and
trickplay image playlists from the .NET API.

## Ownership

- Playback negotiation lives in `apps/backend/src/Prismedia.Infrastructure/Videos`.
- Public playback routes live in `apps/backend/src/Prismedia.Api/Endpoints`.
- The Svelte player adapts API responses in `apps/web-svelte/src/lib/entities/video-capabilities.ts`.
- Player load/reload behavior lives in `apps/web-svelte/src/lib/player/video-player-load.ts`.

Do not add SvelteKit streaming routes or TypeScript HLS builders.
