---
sidebar_position: 3
title: Connecting Infuse & Manet
description: Add Prismedia as a Jellyfin server in client apps and play your library.
---

# Connecting Infuse & Manet

Before you start, make sure you have:

- Prismedia reachable from the client device (e.g. `http://192.168.1.10:8008`).
- A **Jellyfin profile** username and the **API key** — both from **Settings → API Access**. See [Profiles, API Key & NSFW Servers](./profiles.md).

In every client the pattern is the same: add a **Jellyfin** (Emby/Jellyfin) server, point it at Prismedia's URL, and sign in with the **profile name** as the user and the **API key** as the password.

## Infuse (video + audio)

1. Open Infuse → **Add Files / Source → Jellyfin** (or Emby).
2. **Address:** your Prismedia URL and port, e.g. `http://192.168.1.10:8008`.
3. **Username:** a Jellyfin profile name (e.g. `Me`).
4. **Password:** the Prismedia API key.
5. Save. Infuse lists your Movies, Series, Videos, and Collections with artwork.
6. Play a title — compatible files direct-play; others transcode to HLS on demand. Resume position and watched state sync back to Prismedia.

To run a SFW and an NSFW view, add Prismedia **twice** in Infuse, signing in as two different profiles (one with Allow NSFW off, one on).

## Manet / Finamp / Symfonium (audio)

1. Add a **Jellyfin server** in the app.
2. **Server URL:** `http://192.168.1.10:8008`.
3. **Username:** a Jellyfin profile name.
4. **Password:** the Prismedia API key.
5. Browse the Music library — artists, albums, and tracks with cover art, track/disc numbers, and durations.
6. Play — common formats stream directly, others transcode on the fly. Position and play counts sync.

## Troubleshooting

| Symptom | Likely cause / fix |
| --- | --- |
| Can't reach the server | Use the LAN IP and port `8008`, not `localhost`, from another device. Confirm the port is published. |
| Sign-in fails | Username must be an **enabled** profile; password is the **API key** (not a per-user password). Re-copy the key from Settings → API Access. |
| Everything signed out at once | The API key was regenerated, which invalidates all sessions. Sign in again with the new key. |
| Adult content shows when it shouldn't (or vice-versa) | Check the profile's **Allow NSFW** setting; that profile is the one this client signed in as. |
| No artwork | Image requests are anonymous by design; if covers are missing, confirm the items have artwork in Prismedia and that the proxy (if any) isn't blocking `/Items/.../Images`. |
| Behind a reverse proxy and clients can't connect | The Jellyfin routes must bypass the proxy's SSO. See [Reverse Proxy & Auth Middleware](../deployment/reverse-proxy.md). |

For deeper diagnostics see [Troubleshooting](../advanced/troubleshooting.md).
