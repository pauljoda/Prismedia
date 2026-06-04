---
sidebar_position: 1
title: Authentication & API Keys
description: How Prismedia authenticates the web app, API calls, and Jellyfin clients.
---

# Authentication & API Keys

Prismedia is built for a trusted user or household on a private LAN. It has two things to keep in mind: the **API key** that guards programmatic and Jellyfin access, and the **secret** that encrypts stored plugin credentials.

## The web app is frictionless

Browsing the app in a browser needs no manual login. On the first page load the server sets a same-origin, **HttpOnly** cookie (`prismedia-api-key`) carrying the API key, so normal browser use just works. The cookie is `Secure` when the request is over HTTPS.

This means anyone who can reach `http://host:8008` in a browser can use the UI. Prismedia does not provide per-user accounts for the web app — if you need to gate browser access, put it behind a [reverse proxy with auth](./reverse-proxy.md) or keep it on a trusted LAN.

## The API key

A human-typeable API key is generated on first boot and managed in **Settings → API Access**:

- **Get / reveal / copy** — to paste into a script or a Jellyfin client.
- **Regenerate** — issues a new key, rotates the web app's cookie, and **invalidates all Jellyfin sessions** (clients re-authenticate).

The key is required for:

- **`/api/*` routes** (except `/api/health`, which is public for health checks).
- **Jellyfin-compatible routes**, where a profile signs in using the key as its password and then uses a session token. See [Jellyfin Profiles](../jellyfin/profiles.md).

### Supplying the key

For direct API calls, send the key any of these ways:

| Method | Example |
| --- | --- |
| Dedicated header | `X-Prismedia-Api-Key: <key>` |
| Bearer token | `Authorization: Bearer <key>` |
| Query string | `?ApiKey=<key>` or `?api_key=<key>` |
| Cookie | `prismedia-api-key=<key>` (what the browser uses) |

Jellyfin clients additionally use the standard `X-Emby-Authorization` (with `Token=`), `X-Emby-Token`, and `X-MediaBrowser-Token` headers — handled automatically by those apps.

```bash
curl -H "X-Prismedia-Api-Key: $PRISMEDIA_API_KEY" \
  http://localhost:8008/api/library/stats
```

### Rate limiting

Repeated failed key attempts from an address are throttled and return `429 Too Many Requests`, so a leaked URL can't be brute-forced quickly.

## Public (no-key) routes

A small set of routes are intentionally reachable without a key, so health checks and Jellyfin sign-in work:

```text
/api/health
GET  /System/Info/Public
GET/POST /System/Ping
GET  /Branding/Configuration
GET  /Branding/Css   /Branding/Css.css
GET  /QuickConnect/Enabled
GET  /Users/Public
POST /Users/AuthenticateByName
POST /Users/{id}/Authenticate
GET  /Items/{id}/Images/...          (artwork is anonymous, like real Jellyfin)
```

Everything else under `/api/*` and the Jellyfin route prefixes requires a valid key or session token.

## The encryption secret (`PRISMEDIA_SECRET`)

Plugin credentials (for example a TMDB API key) are encrypted at rest with **AES-256-GCM**, using a key derived from `PRISMEDIA_SECRET`.

You normally don't set this. The container's entrypoint:

1. Uses `PRISMEDIA_SECRET` if you provide it.
2. Otherwise reads a previously generated secret from `/data/.prismedia-secret`.
3. Otherwise generates a random secret and persists it to `/data/.prismedia-secret` (mode `600`).

So stored credentials survive container recreation as long as `/data` persists. Set `PRISMEDIA_SECRET` explicitly only if you want to control the key yourself (e.g. to move credentials between environments, or store the secret in a secrets manager).

:::caution
If `PRISMEDIA_SECRET` changes and the old value is gone (the env var changed *and* `/data/.prismedia-secret` was lost), previously encrypted credentials become unreadable and you'll re-enter them. Back up `/data` (which includes the secret file) the same way you back up the database.
:::

## See also

- [Jellyfin Profiles, API Key & NSFW Servers](../jellyfin/profiles.md)
- [Reverse Proxy & Auth Middleware](./reverse-proxy.md)
