---
sidebar_position: 5
title: OPDS Reader Apps
description: Connect third-party ebook and comic readers to Prismedia with OPDS.
---

# OPDS Reader Apps

Prismedia exposes an **OPDS 1.2** catalog for third-party ebook and comic apps. Use it to browse visible books, search, load covers, and download/acquire files from your Prismedia library.

```text
http://your-prismedia-host:8008/opds
```

If Prismedia is behind HTTPS, use the public HTTPS origin instead:

```text
https://prismedia.example.com/opds
```

## Quick setup in a third-party reader

Use these settings in any app that supports OPDS 1.x / OPDS 1.2 catalogs:

| Reader field | Value |
| --- | --- |
| Catalog type | OPDS, OPDS 1.x, or Atom OPDS. |
| Server / catalog URL | `https://prismedia.example.com/opds` or `http://your-prismedia-host:8008/opds`. |
| Authentication | HTTP Basic Auth, if the app asks. |
| Username | Your Prismedia username. |
| Password | Your Prismedia password. |

After saving the catalog, browse **Libraries**, **Recently Added**, **Authors**, or **Series**. Series navigation groups child books; library acquisition feeds only show the individual downloadable books.

:::tip Apps that do not resend credentials for covers/downloads
Some OPDS clients authenticate the catalog request but then fetch cover or download links without the `Authorization` header. If covers or downloads fail while the catalog loads, put a session token query parameter on the catalog URL (get one by signing in with `POST /api/auth/login` — see [Authentication & User Accounts](../deployment/authentication.md#direct-api-access)):

```text
https://prismedia.example.com/opds?access_token=YOUR_SESSION_TOKEN
```

Prismedia will carry that existing token query parameter into OPDS cover and acquisition links. Treat the full URL as a secret.
:::

:::note Books must be in a book-scanning library
A directory only appears in OPDS after it is configured as a Prismedia library root with **Scan books** enabled and the library scan has completed. Video-only, audio-only, image-only, disabled, or unsupported-format roots are excluded from OPDS.
:::

## What the catalog includes

The root catalog links to:

- Recently Added
- Libraries
- Authors
- Series
- Collections
- Tags
- Search

Publication entries include the title, author credits, summary, tags, series metadata, cover/thumbnail links, and one acquisition link when the source file is in a supported format.

## Supported formats

OPDS publishes downloadable books and comics in these formats:

| Format | File extensions | Media type |
| --- | --- | --- |
| EPUB | `.epub` | `application/epub+zip` |
| PDF | `.pdf` | `application/pdf` |
| CBZ / ZIP comics | `.cbz`, `.zip` | `application/vnd.comicbook+zip` or `application/zip` |
| CBR comics | `.cbr` | `application/vnd.comicbook-rar` |

Unsupported book files are excluded from OPDS feeds and return `404` from direct OPDS download links.

## Authentication

Every `/opds` route requires Prismedia authentication. There are no anonymous OPDS catalog, cover, or download URLs.

Most reader apps should use HTTP Basic Auth:

| Field | Value |
| --- | --- |
| Username | Your Prismedia username. |
| Password | Your Prismedia password. |

OPDS also accepts Prismedia session tokens documented in [Authentication & User Accounts](../deployment/authentication.md):

- `Authorization: Bearer <token>`
- `?access_token=<token>`

Some reader apps drop auth headers when they fetch covers or acquisition links. If that happens, configure the app to include a session-token query parameter on the OPDS base URL, for example `https://prismedia.example.com/opds?access_token=...`. Treat URLs containing tokens as secrets.

## NSFW filtering

OPDS uses the same per-user visibility rules as client apps:

- Users without NSFW permission only see SFW books and SFW navigation metadata.
- Users with NSFW permission can see NSFW books if the library is otherwise available.
- Per-user library access applies: a member only sees books from libraries they have been granted.
- Direct book detail, cover, and download routes enforce the same visibility checks as feeds.

Hidden books do not contribute authors, tags, collections, series, counts, covers, search results, or acquisition links.

## Troubleshooting reader setup

| Symptom | Check |
| --- | --- |
| The app shows an SSO login page or cannot add the catalog. | Bypass proxy/forward-auth for `/opds([/?].*)?$` and retry. A request without OPDS credentials should return Prismedia `401`, not an IdP HTML page. |
| The catalog authenticates but shows no books. | Confirm the source directory is a Prismedia library root with **Scan books** enabled, the root is enabled, the scan completed, and the files are EPUB/PDF/CBZ/CBR-compatible. |
| Covers or downloads fail but browsing works. | Use Basic Auth if available. If the reader still drops auth on linked resources, add `?access_token=YOUR_SESSION_TOKEN` to the OPDS base URL. |
| A reader still shows old entries after a fix or rescan. | Refresh/re-sync the OPDS catalog in the app, or remove and re-add the OPDS server. Many readers cache feeds aggressively. |
| NSFW items are missing. | Sign in as a user whose account allows NSFW content. |

## Reverse proxies

If your reverse proxy uses SSO or forward auth, bypass that proxy auth for `/opds([/?].*)?$`. The OPDS routes still require Prismedia authentication, but reader apps cannot complete an interactive SSO redirect.

See [Reverse Proxy](../deployment/reverse-proxy.md) for Authelia, Authentik, Traefik, and Nginx examples.

## Known limitations

OPDS support is catalog, search, cover, and acquisition focused. It does not provide OPDS 2.0 JSON, OPDS-PSE page streaming, annotations, bookmarks, or cross-reader progress sync. Prismedia's built-in reader progress remains available in the web app.
