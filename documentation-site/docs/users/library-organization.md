---
sidebar_position: 3
title: Library Organization
description: How Prismedia classifies videos, books, galleries, and audio from your folder layout.
---

# Library Organization

Prismedia's video classifier reads your folder layout, not your filenames, to decide whether a file is a **movie**, a **flat-series episode**, or a **seasoned-series episode**. Get the layout right up front and you'll spend almost no time fixing classifications later.

The classifier lives in `packages/media-core/src/classifier/classify-video-file.ts` if you want to read the source.

## The depth rule

Depth is measured from the library root (`depth = 0`).

| Depth | Example path | Becomes |
| --- | --- | --- |
| `0` | `/library/Heat (1995).mkv` | **Movie** |
| `1` | `/library/My Show/Episode 01.mkv` | Episode in a **flat series** (synthetic season 0) |
| `2` | `/library/My Show/Season 01/S01E01.mkv` | Episode in a **seasoned series** |
| `3+` | `/library/My Show/Extras/Bonus/clip.mkv` | **Rejected** — too deep, ignored by the scanner |

You can mix all three under the same root, as long as each file's depth is unambiguous. Most people find it easier to put movies and series under separate roots.

## Movies

```text
/library/movies
├── Blade Runner (1982).mkv
├── Heat (1995).mp4
└── No Country for Old Men (2007).mkv
```

Anything directly inside the root is a movie. The filename parser picks up the title and year from `Title (YYYY).ext` patterns.

Good filename forms:

- `Blade Runner (1982).mkv`
- `Blade Runner 1982.mkv`
- `Blade.Runner.1982.1080p.BluRay.mkv`

Bad forms (parser may guess wrong):

- `BR.mkv` — no clue about the title
- `1982 - Blade Runner.mkv` — leading year confuses the regex

You can always edit the title after a scan; the filename parser is a starting point, not a contract.

## Flat series

```text
/library/series
└── My Cool Show
    ├── My Cool Show - 01.mkv
    ├── My Cool Show - 02.mkv
    └── My Cool Show - 03.mkv
```

When files sit **one folder** below the root, that folder is the series and the files are episodes in a synthetic season `0`. Use this when you don't have season subfolders.

The filename parser tries to extract an episode number from common patterns: `S01E03`, `s01e03`, `1x03`, ` - 03`, `Episode 03`.

## Seasoned series

```text
/library/series
└── Another Show
    ├── Season 01
    │   ├── S01E01.mkv
    │   └── S01E02.mkv
    ├── Season 02
    │   ├── S02E01.mkv
    │   └── S02E02.mkv
    └── Specials
        └── christmas-special.mkv
```

When files sit **two folders** below the root, the series is the outer folder and the season is the inner folder. Recognised season-folder forms:

- `Season 01`, `Season 1`, `S01`, `S1`
- `Season 0`, `Specials` (both map to season `0`)

Files in `Specials` get season `0`, episode numbers parsed from the filename.

## Sidecar metadata

When a file is imported, Prismedia merges metadata in this order:

1. **Filename parser** — fallback title, year, season number, episode number.
2. **JSON sidecar** — `<filename>.info.json` next to the file. Keys mirror the YouTube-DL info-json format where applicable.
3. **NFO sidecar** — Kodi/Jellyfin-style `<filename>.nfo`.

User edits in the UI take precedence; **a normal rescan does not overwrite fields you've changed**. If a future pre-1.0 upgrade requires rebuilding metadata, the release notes will say so explicitly.

## What the classifier ignores

The video scanner skips anything it can identify as a generated artifact, sample, or non-media file:

- Filenames containing `-preview`, `_preview`, `-sample`, `_sample`, `-trailer`, `_trailer`
- Filenames matching `*.thumb.*`
- Files at depth `3` or deeper inside a series root
- Files whose extension isn't in `supportedVideoExtensions` (`.mp4`, `.mkv`, `.mov`, `.webm`, `.avi`, `.wmv`, `.flv`, `.ts`, `.m2ts`, `.mpg`, `.mpeg`)

This means you can keep a `_samples/` folder next to a movie without it polluting the library — as long as the sample names contain `-sample` or `_sample`.

## Books

Books are a separate library type from galleries. Enable **Scan books** on a library root to have Prismedia look for archives inside it.

### Supported formats

The book scanner reads `.cbz` and `.zip` archives. Plain image folders are galleries (see below), not books.

### Standalone book

An archive at the **root level** (not inside a subfolder) becomes a single-chapter book. The book title is taken from `ComicInfo.xml` inside the archive if present, otherwise from the filename.

```text
/library/books
├── The Arrival.cbz           → book: "The Arrival"  (1 chapter)
├── Watchmen.zip              → book: "Watchmen"      (1 chapter)
└── My Manga Vol 3.cbz        → book: "My Manga Vol 3" (1 chapter)
```

### Multi-chapter series

Archives **inside a subfolder** are grouped into one book. The subfolder name becomes the book title; each archive becomes a chapter. Chapter numbers are read from `ComicInfo.xml` or inferred from the trailing number in the filename.

```text
/library/books
└── Saga
    ├── Saga 001.cbz          → chapter 1
    ├── Saga 002.cbz          → chapter 2
    └── Saga 003.cbz          → chapter 3
```

A root can mix standalone archives and series folders at the same time:

```text
/library/books
├── The Arrival.cbz           → standalone book
└── Saga
    ├── Saga 001.cbz
    └── Saga 002.cbz
```

### Volume folders

For manga and long comic runs, you can organize chapter archives into volume folders. Prismedia treats the folder structure as the source of truth: the book folder contains volume folders, and each volume folder contains chapter archives.

```text
/library/books
└── The Promised Neverland
    ├── Volume 01
    │   ├── The Promised Neverland 001.cbz
    │   ├── The Promised Neverland 002.cbz
    │   └── The Promised Neverland 003.cbz
    ├── Volume 02
    │   ├── The Promised Neverland 008.cbz
    │   └── The Promised Neverland 009.cbz
    └── The Promised Neverland 185.cbz  → loose chapter
```

Recognized volume folder names include:

- `Volume 01`, `Volume 1`
- `Vol. 1`, `Vol 01`
- `v01`, `V1`
- `Book 1`

Volume folders are internal groups, not separate books. The book detail page shows volumes first when they exist; selecting a volume opens the chapters in that volume, and selecting a chapter opens its pages. Chapters that are not inside a recognized volume folder stay loose and remain visible alongside the volume groups.

You can self-organize volumes by moving archives on disk and rescanning. Identify providers can also suggest volume grouping; accepting those suggestions moves matched chapter archives into volume folders using collision-safe moves. Prismedia checks every destination before moving anything, and only updates the database after the filesystem moves succeed.

Volume covers are stored on the volume group, not copied onto every chapter. You can edit a volume cover from the book edit view just like chapter covers.

### ComicInfo.xml

If an archive contains a `ComicInfo.xml` file, Prismedia reads it during the scan:

| ComicInfo field | Used as |
| --- | --- |
| `<Series>` | Book title (overrides folder name) |
| `<Title>` | Chapter title (overrides filename) |
| `<Number>` | Chapter number |
| `<Volume>` | Volume number when archives are otherwise flat |
| `<Publisher>` | Studio |
| `<Writer>`, `<Penciller>`, `<Artist>`, etc. | Performers (creators) |
| `<Genre>`, `<Tags>` | Tags |
| `<Summary>` | Book description |
| `<AgeRating>`, `<Manga>` | Used to infer NSFW flag |

Fields already edited in the UI are not overwritten by a rescan.

### Reading progress

Prismedia tracks read/unread progress per chapter. The reader opens in paged (spread) mode or vertical webtoon mode; your last-used mode persists per book. Chapter-to-chapter navigation flows without leaving the reader.

---

## Galleries, images, and audio

Video classification is depth-based; the other media types use simpler rules.

### Galleries

A **gallery** is a **folder of images** under a root with `scan_galleries` enabled. Each folder of images becomes one gallery.

Images directly inside a root become loose images, not a gallery. Group them in a subfolder if you want gallery semantics.

Page images are sorted with natural filename ordering (`page2.jpg` before `page10.jpg`).

:::note
Comic and manga archives (`.cbz`, `.zip`) are part of the **Books** library, not Galleries. Enable **Scan books** on the root — not **Scan galleries** — to pick them up.
:::

### Images

Files matching the supported image formats (JPEG, PNG, WebP, AVIF, HEIF, GIF) are imported individually if they are not part of a gallery folder. They are scanned by roots with `scan_images` enabled.

### Audio

Audio scans look for **library folders**. The convention mirrors music libraries:

```text
/library/audio
├── Some Album
│   ├── 01 - Track One.mp3
│   ├── 02 - Track Two.mp3
│   └── cover.jpg
└── Another Album
    └── 01 - Single Track.flac
```

Each folder becomes an audio library; tracks inside become audio tracks. ID3 tags and embedded cover art are read during the audio probe job.

## When to rescan

You don't need to rescan after every change. The scanner is idempotent on file paths — running it again is safe.

Rescan when:

- You added or removed files on disk
- You moved files between folders (re-classification will pick up the new layout)
- You enabled or disabled scan flags on a library root
- A release note asked you to rebuild metadata after an upgrade

Trigger scans manually from the **Operations** page (sidebar → **Jobs** → **Library scan** queue → **Run**), or set **Auto-scan** in Settings.
