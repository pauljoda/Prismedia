---
sidebar_position: 1
title: How Scanning Works
description: How Prismedia walks watched roots, what it skips, and how files become entities.
---

# How Scanning Works

Prismedia treats your folder layout as the source of truth. You decide which folders are **watched roots** and which media types each root scans; the scanner walks those folders and turns files into entities.

This page covers the mechanics shared by every media type. The per-type rules — with the exact extension lists and folder trees — are on the dedicated pages:

- [Videos, Movies & Series](./videos.md)
- [Images & Galleries](./images-galleries.md)
- [Books, Comics & eBooks](./books.md)
- [Audio & Music](./audio.md)

## Watched roots and scan types

A watched root is a container path plus per-type scan toggles. A scan walks the root (recursively, unless **Recursive** is off) and dispatches each file to the scanner for an enabled media type.

| Toggle | Looks for |
| --- | --- |
| **Videos** | Video files → movies, series, seasons, episodes. |
| **Images** | Image files → loose images and galleries. |
| **Books** | Comic archives (`.cbz`/`.zip`) and single-file books (`.epub`/`.pdf`). |
| **Audio** | Audio files → artists, albums, tracks. |

Keep folders that need different behavior (different media types, or NSFW vs not) as separate roots.

## What the scanner skips

Three things are never turned into entities:

1. **Hidden folders.** Any directory whose name starts with `.` (for example `.thumbs`, `.AppleDouble`, `.git`) is skipped entirely, along with everything inside it.
2. **Generated artifacts.** Files whose name (before the extension) ends in one of these suffixes — with a `-`, `_`, or `.` separator — are treated as derivative media and ignored:

   ```text
   -preview  _preview  .preview
   -thumb    _thumb    .thumb
   -sprite   _sprite   .sprite
   -sample   _sample   .sample
   ```

   So `movie-preview.mkv` and `cover.thumb.jpg` are skipped; `Heat (1995).mkv` is not.
3. **Excluded paths** (see below).

Unsupported extensions are simply not picked up by any scanner.

## Scan exclusions

Use **Files → Exclude** to keep a path out of future scans without deleting it from disk. Exclusions are reversible from the same context menu.

- Exclusions are **literal paths**, not glob patterns.
- Excluding a folder also excludes everything beneath it (`/media/movies/skip` hides `/media/movies/skip/anything`).
- Entities that fall under a newly excluded path are removed on the next scan; removing the exclusion and rescanning brings them back.

## Incremental scans

Each scan records the files it saw under a root (by size and modified time). The next scan compares the folder against that record and, when nothing was added, removed, or changed, **skips straight past the detailed work** instead of re-examining every file. Scheduled rescans of large, unchanged libraries finish almost instantly; the first scan, and any scan where files actually changed, does the full pass.

:::note
Editing a metadata sidecar (for example an `.nfo`) without touching its media file is picked up on the next real change or an explicit entity refresh — not on an otherwise-unchanged rescan.
:::

There is at most **one scan per media kind** in flight at a time. A scheduled scan, a newly added folder, and a manual "scan" all reuse the in-flight scan for that kind rather than stacking duplicates.

## Sidecar and embedded metadata

The scanner imports metadata that sits alongside or inside your files, so newly scanned items arrive with real titles and details before any provider identify:

| Media type | Source | Notes |
| --- | --- | --- |
| Video / Movie | `*.nfo`, `movie.nfo`, `*.info.json`, `*.json`, `movie.info.json`, `movie.json` | Title, description, date, studio, URLs, tags, cast/crew, duration. |
| Comic | `ComicInfo.xml` inside the archive | Title, series, issue/volume, summary, date, publisher, creators, tags, age rating. |
| eBook | Inside the `.epub`/`.pdf` | Title, author, description; cover from the EPUB image or the PDF's first page. |
| Audio | Embedded tags (read at probe time) | Title, artist, album, track/disc numbers, cover art. |

User edits made in the app are intentional library state. Routine rescans preserve your edits, ratings, visibility, relationships, and playback/reading progress.

## When to rescan

Rescan when:

- Files were added, moved, renamed, or deleted outside Prismedia.
- You changed scan toggles on a root.
- You added sidecar metadata.
- You removed an exclusion.
- You want Prismedia to reconcile linked file state after manual cleanup.

Scans are idempotent and safe to run repeatedly. Run one from **Jobs**, **Settings → Watched Libraries**, or **Files**.
