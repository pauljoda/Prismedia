---
sidebar_position: 2
title: Profiles, API Key & NSFW Servers
description: Create Jellyfin sign-in profiles, manage the API key, and split SFW/NSFW access.
---

# Profiles, API Key & NSFW Servers

Jellyfin clients sign in with a **username and password**. Prismedia maps that onto two things:

- **Jellyfin profiles** — lightweight "fake users" you create. The username is the profile name.
- **The app API key** — used as the **password** for every profile.

There are no per-user passwords; the single app API key authenticates all profiles. What differs between profiles is their **name** and their **NSFW visibility**.

## The API key

A human-typeable API key is generated on first boot. Manage it in **Settings → API Access**:

- **Reveal / copy** the key to paste into a client.
- **Regenerate** the key — this immediately **invalidates all existing Jellyfin sessions** (clients must sign in again) and also rotates the key the web app uses.

The same key authenticates direct `/api/*` calls. See [Authentication & API Keys](../deployment/authentication.md).

## Creating profiles

In **Settings → API Access**, add a profile with:

| Field | Meaning |
| --- | --- |
| **Username** | The name the client signs in as (must be unique). |
| **Display name** | Optional friendlier label. |
| **Allow NSFW** | Whether this profile sees NSFW-flagged content. |
| **Enabled** | Disabled profiles cannot sign in. |

You can edit or delete profiles at any time. Deleting or disabling a profile, or regenerating the API key, ends its sessions.

## NSFW "servers"

Because NSFW visibility is **per profile**, you can present the same library two ways and add each as a **separate server** in your client app:

```text
Profile "Family"     Allow NSFW = off   →  client server A (no adult content)
Profile "Me"         Allow NSFW = on    →  client server B (everything)
```

In Infuse/Manet you add two Jellyfin servers pointing at the same Prismedia URL, signing in as each profile. The "Family" server never shows NSFW items (they're filtered out of listings, search, artwork, and playback); the "Me" server shows everything.

This mirrors Prismedia's own [content-visibility](../using/settings.md#content-visibility) model — the profile's setting takes the place of the browser's visibility mode for that client.

## How sign-in works

1. The client sends the profile **username** and the **API key** as the password to `/Users/AuthenticateByName`.
2. Prismedia verifies the username is an enabled profile and the password matches the API key.
3. It issues a session token the client stores and sends on every request.
4. Each request is resolved back to its profile, and NSFW filtering is applied accordingly.

Continue to [Connecting Infuse & Manet](./clients.md).
