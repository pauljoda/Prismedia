---
sidebar_position: 2
title: Your First Library & Scan
description: Add a watched library root, choose what it scans, run the scan, and verify the result.
---

# Your First Library & Scan

After the container starts, Prismedia needs at least one **watched library root**. A root is a folder inside the container — usually somewhere under `/media` — plus the media types you want scanned from that folder.

## 1. Open the app

Open [http://localhost:8008](http://localhost:8008). The container serves the built web app and API from the same port.

## 2. Add a watched root

Go to **Settings → Watched Libraries** and add a root such as `/media`, `/media/movies`, or `/media/books`.

![Settings](/img/screenshots/settings.png)

:::tip[Use the path inside the container]
The root path is the path **inside the container**, not on the host. If you mounted `/srv/movies:/media/movies`, the root path is `/media/movies`.
:::

Choose the scan toggles that match the folder:

| Toggle | Scans |
| --- | --- |
| **Videos** | Video files → movies, series, seasons, and episodes. |
| **Images** | Loose image files and folders of images (galleries). |
| **Books** | `.cbz`/`.zip` comics, `.epub`/`.pdf` eBooks, and `.m4b`/`.m4a`/`.mp3` audiobooks. |
| **Audio** | Music folders → artists, albums, and tracks. |

Other per-root settings:

| Setting | Meaning |
| --- | --- |
| **Enabled** | Disabled roots stay configured but are skipped by scans. |
| **Recursive** | Walk subfolders. Leave on unless the root is a single flat folder. |
| **NSFW** | Marks all media under the root as restricted by default. |
| **Auto Identify** | Whether this root participates in automatic identification during scans (see [Identify & Enrich](./identify-walkthrough.md)). |

Use separate roots when different folders need different scan behavior or visibility. How files become entities is covered in detail in [Library & Scanning](../library/overview.md).

## 3. Run the first scan

A newly added root starts scanning immediately for exactly the media kinds it has enabled. You can also trigger scans manually from:

- **Jobs**, for a general library scan.
- **Settings → Watched Libraries**, for root-level management.
- **Files**, for a specific root, folder, or file context.

![Jobs](/img/screenshots/jobs.png)

The worker enqueues follow-up jobs for probing, thumbnails, waveforms, sprites, subtitles, HLS assets, and metadata imports as needed. Scans are **incremental** — an unchanged root finishes almost instantly on the next pass, while a changed root does the full work.

## 4. Verify in Browse and Files

Use the browse pages for catalog views:

- **Movies**, **Series**, and **Videos**
- **Galleries** and **Images**
- **Comics** and **eBooks**
- **Audio** and **Artists**
- **People**, **Studios**, and **Tags**
- **Collections**

Use **Files** to inspect the source folder layout, linked entities, exclusions, and file operations.

![Files](/img/screenshots/files.png)

## 5. Set visibility

Prismedia is designed for private LAN use, but some libraries may still contain content you do not want shown by default. Mark roots or entities as NSFW where appropriate and control visibility from **Settings → Content Visibility**.

Visibility is enforced across browse pages, search, files, identify, jobs context, relationship rails, and Jellyfin clients. See [Settings](../using/settings.md).

## 6. Identify metadata

Once your media is scanned, open **Identify** (or use a detail page's Identify action) to fetch provider metadata, review the proposal, and accept the fields and artwork you want.

Walk through it in [Identify & Enrich Your Media](./identify-walkthrough.md).
