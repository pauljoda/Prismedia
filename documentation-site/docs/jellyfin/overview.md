---
sidebar_position: 1
title: Jellyfin Compatibility
description: Serve your Prismedia library to Jellyfin client apps like Infuse and Manet.
---

# Jellyfin Compatibility

Prismedia speaks a **Jellyfin-compatible API** so third-party Jellyfin client apps can discover your server, sign in, browse, and play your library — without Prismedia being a Jellyfin server or storing a Jellyfin schema.

:::caution Experimental
This is an experimental compatibility layer, not a full Jellyfin implementation. It is built and tested against specific clients (below). Other Jellyfin apps may work partially or not at all, and behavior can change between releases.
:::

## Tested clients

| Client | Video | Audio |
| --- | --- | --- |
| **Infuse** | ✅ | ✅ |
| **Manet** | — | ✅ |
| **Finamp** | — | ✅ |
| **Symfonium** | — | ✅ |

Video playback is tested through Infuse (direct play of compatible files, on-demand HLS otherwise). Music plays through Infuse and dedicated music clients (Manet, Finamp, Symfonium, and the official Jellyfin apps).

## What works

- **Discovery & sign-in** — clients find the server and authenticate with a Jellyfin profile (a "fake user") plus the app API key. See [Profiles, API Key & NSFW Servers](./profiles.md).
- **Browsing** — Movies, Series (with seasons/episodes), Videos, and a Music library of artists, albums, and tracks; collections and "Next Up"/resume shelves; poster, backdrop, logo, and thumbnail artwork.
- **Video playback** — direct play for files the client can decode, transcoded HLS otherwise, with rich media-source/codec metadata so capable clients (e.g. Infuse) direct-play 4K HEVC / Dolby Vision.
- **Audio playback** — direct for common formats, transcoded on the fly otherwise.
- **Progress sync** — resume position, completion, and play counts sync both ways with Prismedia's native player, so where you stop in Infuse is where you resume in the browser.
- **NSFW filtering** — each profile independently shows or hides NSFW content, so you can run separate SFW and NSFW "servers" in your client. See [Profiles, API Key & NSFW Servers](./profiles.md).

## How it fits together

```text
Jellyfin client (Infuse / Manet)
    │  Jellyfin API + token
    ▼
.NET API  ── /System  /Users  /Items  /Videos  /Audio  /Sessions  /Library  …
    │
    ├─ authenticates the token to a Jellyfin profile
    ├─ filters NSFW per the profile's setting
    └─ streams from Prismedia's existing video/audio pipeline
```

The Jellyfin routes are protected by Prismedia's own token auth — they are **not** part of the `/api/*` surface. If you put Prismedia behind a reverse-proxy/SSO middleware, those routes must bypass the proxy's login so clients can authenticate. See [Reverse Proxy & Auth Middleware](../deployment/reverse-proxy.md).

## Next

- [Profiles, API Key & NSFW Servers](./profiles.md) — create the "users" your clients sign in as.
- [Connecting Infuse & Manet](./clients.md) — step-by-step client setup.
