---
sidebar_position: 2
title: Reverse Proxy
description: Run Prismedia behind a TLS reverse proxy or forward-auth middleware.
---

# Reverse Proxy

Prismedia listens on one port (`8008`) and serves the web app, native `/api/*` routes, playback assets, and the OPDS catalog. A normal TLS reverse proxy can forward that entire origin unchanged.

Prismedia already has per-user accounts and does not require Authelia, Authentik, or other forward-auth middleware. For most private deployments, a plain TLS proxy is the simplest arrangement.

## Forward standard headers

Forward `Host`, `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host` so Prismedia sees the public scheme and host and assigns rate-limit buckets correctly. There is no `PUBLIC_ORIGIN` or base-URL variable; configure the public URL at the proxy.

Preserve request methods, query strings, `Authorization`, `Range`, and response streaming. Video source routes under `/api/playback` use byte ranges, while HLS playlists and segments are streamed as normal API responses.

## Forward-auth and non-browser clients

Interactive SSO redirects work for a browser but not for API clients or OPDS readers. If forward-auth middleware protects the Prismedia host, either:

1. use Prismedia's own login without the extra middleware; or
2. bypass forward-auth for `/api/*` and `/opds`, allowing Prismedia to authenticate those routes itself.

Bypassing the external middleware does not make protected API routes anonymous. Prismedia still requires its session cookie or bearer token on `/api/*`, except for health and setup/login routes. OPDS still requires Basic Auth.

## Authelia

Place the bypass rules above the normal `one_factor` or `two_factor` rule:

```yaml
# configuration.yml
access_control:
  default_policy: deny
  rules:
    - domain: prismedia.example.com
      policy: bypass
      resources:
        - '(?i)^/api([/?].*)?$'
        - '(?i)^/opds([/?].*)?$'

    - domain: prismedia.example.com
      policy: one_factor
```

The `([/?].*)?` suffix preserves matches when a client sends either a subpath or a query string. Restart Authelia after changing `configuration.yml`; it does not hot-reload that file.

## Authentik

Add these expressions to the Prismedia Proxy Provider's **Unauthenticated Paths**:

```text
(?i)^/api([/?].*)?$
(?i)^/opds([/?].*)?$
```

The Prismedia web shell can remain behind Authentik while native API and OPDS requests reach Prismedia's own authentication middleware.

## Traefik and Nginx

With Traefik forward-auth or Nginx `auth_request`, use separate routers or locations for `/api` and `/opds` without the external auth middleware. Route every path to the same Prismedia service on port `8008`.

Do not cache authenticated API, playback, or OPDS responses at the proxy. Static application assets may use Prismedia's own cache headers.

## Verify the proxy

```bash
# Public health check: no redirect and a successful response.
curl -i https://prismedia.example.com/api/health

# Protected native API: Prismedia 401, not an HTML SSO redirect.
curl -i https://prismedia.example.com/api/auth/me

# Native sign-in: Prismedia JSON response, not an IdP page.
curl -i -X POST https://prismedia.example.com/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"your-username","password":"your-password"}'

# OPDS without credentials: Prismedia 401 with a Basic challenge.
curl -i https://prismedia.example.com/opds

# OPDS with Prismedia credentials: an Atom feed.
curl -i -u "your-username:your-password" https://prismedia.example.com/opds
```

If a request returns an HTML login page or a `302`/`303` to an identity provider, the forward-auth bypass did not match. Check the path rule and restart the auth proxy after configuration changes.
