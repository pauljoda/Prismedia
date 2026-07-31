---
sidebar_position: 1
title: Authentication & User Accounts
description: How Prismedia authenticates the web app, native API calls, and OPDS readers with per-user accounts.
---

# Authentication & User Accounts

Prismedia uses its own user accounts. The web app and native `/api/*` routes use per-user sessions; OPDS readers authenticate with the same username and password. There is no shared application API key.

## First-run setup

A fresh install shows a **setup wizard** on first visit. It creates the administrator account and signs you in. Until an administrator exists, the app serves only setup, so complete it promptly after exposing the server.

Upgrading from a pre-2.0 install? See [the upgrade notes](#upgrading-from-pre-20). Legacy sign-in profiles become normal Prismedia accounts automatically.

## Accounts and roles

There are two roles:

- **Administrators** manage the server, settings, users, libraries, files, jobs, identify, requests, and plugins. They implicitly see every library.
- **Members** browse and play. For each member, an administrator controls which libraries they can see, whether they may view NSFW content, and whether they may create libraries.

Manage accounts in **Settings → Users**. Each user changes their own display name, password, content visibility, and connected devices on the **Account** page. The last enabled administrator cannot be demoted, disabled, or deleted.

## Web sessions

Signing in at `/login` sets a same-origin, **HttpOnly** cookie named `prismedia-session`. It is marked `Secure` over HTTPS. Sessions use a sliding lifetime, so an active household browser remains signed in; users can inspect and revoke their own device sessions from the Account page.

## Direct API access

Scripts and native clients sign in through Prismedia's own API, then send the returned token as a bearer token:

```bash
TOKEN=$(curl -s -X POST http://localhost:8008/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"you","password":"your-password"}' | jq -r .accessToken)

curl -H "Authorization: Bearer $TOKEN" http://localhost:8008/api/library/stats
```

The native transports are:

| Client | Transport |
| --- | --- |
| Web app | `prismedia-session=<token>` cookie |
| Script or native app | `Authorization: Bearer <token>` |
| Playback URL that cannot set a header | `?access_token=<token>` |
| OPDS reader | HTTP Basic Auth with the user's username and password |

Prefer the bearer header whenever possible. URLs containing `access_token` are secrets and may appear in logs or browser history. Revoke a device's session from the Account page if a token leaks.

## OPDS readers

Most OPDS readers use HTTP **Basic Auth** with the same Prismedia account credentials:

```bash
curl -u "you:your-password" http://localhost:8008/opds
```

Per-user library access and NSFW permissions apply to OPDS feeds, covers, and downloads just as they do in the web app. See [OPDS Reader Apps](../library/opds.md) for clients that need a token-bearing catalog URL for linked resources.

## Rate limiting

Repeated failed sign-in attempts from an address are throttled and return `429 Too Many Requests`.

## Public routes

Only the setup/login surface and health check are public:

```text
GET  /api/health
GET  /api/auth/setup-status
POST /api/auth/setup          (only while no administrator exists)
POST /api/auth/login
```

All other `/api/*` routes require a valid Prismedia session. Every `/opds` route requires authentication and returns a Basic Auth challenge when credentials are missing.

## Upgrading from pre-2.0

Upgrading a pre-2.0 install migrates legacy sign-in data automatically:

- Existing sign-in profiles become member accounts with access to the existing libraries.
- Their previous server credential becomes the initial password so existing account owners can sign in and choose a new one.
- The next browser visit shows setup so you can create the administrator account. Reusing a migrated username promotes that account.
- Existing watch history, favorites, and ratings are preserved for migrated accounts.

Set new per-user passwords from the setup flow or **Settings → Users**.

## Password recovery

Locked out? Set environment variables on the container and restart:

| Variable | Effect |
| --- | --- |
| `PRISMEDIA_RECOVERY_PASSWORD` | On boot, resets or creates an enabled administrator with this password and signs out its other sessions. |
| `PRISMEDIA_RECOVERY_USERNAME` | The account to reset or create. Defaults to `admin`. |

The reset repeats on every boot while the variable is set and logs a warning. Unset it after signing back in.

## The encryption secret (`PRISMEDIA_SECRET`)

Plugin credentials, such as a metadata-provider API key, are encrypted at rest with AES-256-GCM using a key derived from `PRISMEDIA_SECRET`.

You normally do not set this yourself. The container entrypoint:

1. Uses `PRISMEDIA_SECRET` if you provide it.
2. Otherwise reads a previously generated secret from `/data/.prismedia-secret`.
3. Otherwise generates a random secret and persists it to `/data/.prismedia-secret` with mode `600`.

Stored credentials therefore survive container recreation as long as `/data` persists. Set `PRISMEDIA_SECRET` explicitly only when you need to control the key yourself, such as through a secrets manager.

:::caution
If the encryption secret changes and the old value is lost, previously encrypted plugin credentials become unreadable and must be entered again. Back up `/data`, including the secret file, with the database.
:::

## See also

- [OPDS Reader Apps](../library/opds.md)
- [Reverse Proxy](./reverse-proxy.md)
