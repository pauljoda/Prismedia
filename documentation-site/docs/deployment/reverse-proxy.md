---
sidebar_position: 2
title: Reverse Proxy & Auth Middleware
description: Run Prismedia behind a proxy and keep Jellyfin clients working through SSO.
---

# Reverse Proxy & Auth Middleware

Prismedia listens on a single port (`8008`) and serves the web app, `/api/*`, and the Jellyfin-compatible routes from it. Putting it behind a reverse proxy (Caddy, Nginx, Traefik) for TLS is straightforward. The wrinkle is **auth middleware** — Authelia, Authentik, or any forward-auth SSO.

## The problem with SSO and Jellyfin clients

SSO middleware protects a site by redirecting unauthenticated requests to a login page and setting a session cookie. That works for browsers, but **Jellyfin client apps (Infuse, Manet, …) cannot complete an interactive browser login.** They authenticate to Prismedia directly with their own token (the API key → session token flow).

So if your SSO covers the Jellyfin routes, clients get redirected to a login page instead of an API response, and they fail to connect.

**The fix:** exclude the entire Jellyfin route surface from the SSO middleware. Those routes are already protected by Prismedia's own token authentication, so bypassing the proxy's SSO does not expose them — a caller still needs a valid Prismedia API key / session token. See [Authentication & API Keys](./authentication.md).

## Routes to bypass

Exclude these route surfaces from your auth middleware:

```text
/System
/Users
/UserViews
/Items
/Shows
/Artists
/Videos
/Audio
/Sessions
/UserPlayedItems
/UserItems
/MediaSegments
/Library
/Branding
/QuickConnect
/DisplayPreferences
```

Also leave `/api/health` reachable for health checks.

:::warning Match these case-insensitively, and allow the `/emby` and `/jellyfin` prefixes
Real clients do **not** request these paths in the casing shown above. Infuse and other Emby-mode clients send lowercase (`/users/{id}/items/resume`) and some prefix every request with `/emby` or `/jellyfin` (`/emby/Users/AuthenticateByName`). If your proxy matches with a **case-sensitive** regex anchored at `^/Users`, the login and sync calls slip past the bypass, get redirected to the login page, and the client reports a connection/sync failure.

The Authelia and Authentik examples below use `(?i)` (case-insensitive) and an optional `(/emby|/jellyfin)?` prefix for exactly this reason — keep both.
:::

You can keep SSO on the **web app** (the SPA at `/` and `/api/*`) if you want browser users to authenticate through your IdP first — that does not affect Jellyfin clients, which only use the prefixes above. (Note the web app already authenticates itself with a cookie once it loads; SSO simply gates who can reach it.)

## Forward standard proxy headers

Forward `Host`, `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host` so HTTPS detection and rate-limiting buckets behave. There is no `PUBLIC_ORIGIN`/base-URL variable to set in Prismedia — configure URLs at the proxy.

## Authelia

Authelia decides per-request via access-control rules. Put a `bypass` rule for the Jellyfin paths **above** your normal `one_factor`/`two_factor` rule so it wins:

```yaml
# configuration.yml
access_control:
  default_policy: deny
  rules:
    # Jellyfin clients authenticate to Prismedia directly — bypass SSO for them.
    # (?i) = case-insensitive; (/emby|/jellyfin)? tolerates clients that prefix
    # every request, so lowercase /users/... and /emby/Users/... both match.
    - domain: prismedia.example.com
      policy: bypass
      resources:
        - '(?i)^(/emby|/jellyfin)?/System(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/Users(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/UserViews(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/Items(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/Shows(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/Artists(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/Videos(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/Audio(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/Sessions(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/UserPlayedItems(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/UserItems(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/MediaSegments(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/Library(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/Branding(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/QuickConnect(/.*)?$'
        - '(?i)^(/emby|/jellyfin)?/DisplayPreferences(/.*)?$'
        - '(?i)^/api/health$'

    # Everything else (web app + /api) requires login.
    - domain: prismedia.example.com
      policy: one_factor
```

:::tip Authelia does not hot-reload `configuration.yml`
After editing the rules, **restart the Authelia container** (`docker restart authelia`). It only watches the users database, not `configuration.yml`, so an un-restarted edit silently keeps the old rules — every request, even correctly-cased ones, keeps redirecting to login.
:::

## Authentik

In the Authentik **Proxy Provider** for Prismedia, add the Jellyfin prefixes to **Unauthenticated Paths** (regular expressions, one per line). Requests matching them skip the forward-auth check:

```text
(?i)^(/emby|/jellyfin)?/System
(?i)^(/emby|/jellyfin)?/Users
(?i)^(/emby|/jellyfin)?/UserViews
(?i)^(/emby|/jellyfin)?/Items
(?i)^(/emby|/jellyfin)?/Shows
(?i)^(/emby|/jellyfin)?/Artists
(?i)^(/emby|/jellyfin)?/Videos
(?i)^(/emby|/jellyfin)?/Audio
(?i)^(/emby|/jellyfin)?/Sessions
(?i)^(/emby|/jellyfin)?/UserPlayedItems
(?i)^(/emby|/jellyfin)?/UserItems
(?i)^(/emby|/jellyfin)?/MediaSegments
(?i)^(/emby|/jellyfin)?/Library
(?i)^(/emby|/jellyfin)?/Branding
(?i)^(/emby|/jellyfin)?/QuickConnect
(?i)^(/emby|/jellyfin)?/DisplayPreferences
(?i)^/api/health$
```

Keep the web app and `/api/*` authenticated by the provider as usual.

## Traefik / Nginx note

If you use Traefik forward-auth or Nginx `auth_request`, apply the same principle: route the Jellyfin prefixes to Prismedia **without** the auth middleware attached (a separate router/location block), and keep the middleware on the catch-all router that serves the SPA and `/api`.

## Verifying

From a device on your network, after configuring the bypass:

```bash
# Should return JSON without an SSO redirect:
curl -i https://prismedia.example.com/System/Info/Public

# Test the casing/prefix clients actually use — must NOT redirect to your login page.
# A 401 from Prismedia here is fine (no token); a 302 to your IdP means the bypass missed.
curl -i https://prismedia.example.com/users/me/items/resume
curl -i https://prismedia.example.com/emby/System/Info/Public
```

If you get an HTML login page (or a `302`/`303` to your IdP) instead of a JSON or `401` response from Prismedia, the bypass isn't matching — re-check the path patterns, and on Authelia confirm you restarted the container after editing the rules. Then add the server in a client per [Connecting Infuse & Manet](../jellyfin/clients.md).
