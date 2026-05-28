---
sidebar_position: 2
title: First Boot
description: Add your first library root, scan it, and verify the result.
---

# First Boot

After the container starts, Prismedia needs one watched library root. A root is a folder inside the container, usually somewhere under `/media`, plus the media types you want scanned from that folder.

## 1. Open the app

Open [http://localhost:8008](http://localhost:8008). The release container serves the built web app and API from the same port.

## 2. Add a watched root

Go to **Settings -> Watched Libraries**.

![Settings](/img/screenshots/settings.png)

Add a root such as `/media`, `/media/videos`, or `/media/books`.

Choose the scan toggles that match the folder:

| Toggle | Scans |
| --- | --- |
| **Videos** | Video files, movies, series, seasons, and episodes. |
| **Images** | Loose image files. |
| **Galleries** | Folders of images and animated media. |
| **Books** | `.cbz` and `.zip` comic/book archives. |
| **Audio** | Audio library folders and tracks. |

Use separate roots when different folders need different scan behavior.

## 3. Run the first scan

Open **Jobs**, find the library scan action, and run it. You can also rescan a specific root or folder from the **Files** workspace.

![Jobs](/img/screenshots/jobs.png)

The worker will enqueue follow-up jobs for probing, thumbnails, waveforms, sprites, subtitles, HLS assets, pHashes, and metadata imports as needed.

## 4. Verify in Browse and Files

Use the browse pages for catalog views:

- **Videos** and **Series**
- **Images** and **Galleries**
- **Books**
- **Audio**
- **People**, **Studios**, and **Tags**
- **Collections**

Use **Files** when you want to inspect the source folder layout, linked entities, exclusions, and file operations.

![Files](/img/screenshots/files.png)

## 5. Set visibility

Prismedia is designed for private LAN use, but some libraries may still contain content you do not want shown by default. Mark roots or entities as NSFW when appropriate and control visibility from **Settings -> Content Visibility**.

Visibility is enforced across browse pages, search, files, identify, jobs context, and relationship rails.

## 6. Identify metadata

Open **Identify** when you want provider metadata. Add entities to the queue, run providers, review the proposal, and accept only the fields and artwork you want.

![Identify](/img/screenshots/identify.png)

Provider setup lives in **Plugins**.
