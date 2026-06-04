---
sidebar_position: 2
title: Videos, Movies & Series
description: How video files become movies, standalone videos, series, seasons, and episodes.
---

# Videos, Movies & Series

When a watched root has **Videos** enabled, the scanner classifies each video file by **where it sits in the folder tree** and by hints in its filename.

## Supported video extensions

```text
.mp4  .m4v  .mkv  .mov  .webm  .avi  .wmv  .flv  .ts  .m2ts  .mpg  .mpeg
```

Files in other formats are ignored. Files ending in a generated suffix (`-preview`, `-sample`, `-thumb`, `-sprite`, with `-`/`_`/`.` separators) are skipped — see [How Scanning Works](./overview.md#what-the-scanner-skips).

## The four outcomes

Every video resolves to one of four shapes:

| Outcome | Where it lives | Library |
| --- | --- | --- |
| **Movie** | A folder directly under the root containing a single video (no season folders, no episode token). | Movies |
| **Standalone video** | A video sitting loose at the root, or one that matches no other rule. | Videos |
| **Episode** | A video under a series (and optionally a season) folder, or whose filename carries an episode token. | Series → (Season) → Episode |
| **Folder series episode** | One of several loose videos in a sub-folder with no season/episode markers. | Series → Episode (ordered by filename) |

Standalone videos appear in the **Videos** library; movies in **Movies**; episodes under their **Series**. (A movie's playable file still shows in Videos too.)

## Folder layout → entity

```text
/media/videos/
├── Heat (1995).mkv                      → Standalone video  (loose at root)
│
├── John Wick (2014)/
│   └── john.wick.2014.1080p.mkv         → Movie "John Wick (2014)"   (single video in a root-level folder)
│
├── The Office/
│   ├── S01E01 - Pilot.mkv               → The Office · Season 1 · Episode 1
│   └── S01E02 - Diversity Day.mkv       → The Office · Season 1 · Episode 2
│
├── Breaking Bad/
│   ├── Season 01/
│   │   └── S01E01.mkv                    → Breaking Bad · Season 1 · Episode 1
│   └── Season 02/
│       └── S02E03.mkv                    → Breaking Bad · Season 2 · Episode 3
│
└── Nature Shorts/
    ├── Aurora.mkv                        → Nature Shorts · Episode 1  (ordered by filename)
    └── Tides.mkv                         → Nature Shorts · Episode 2
```

### Movie rule

A folder placed **directly under the root** that holds a **single video** becomes a movie, named after the folder. The release filename does **not** need to match the folder name (`Pokemon.The.First.Movie.1998…` in a `Pokemon The First Movie` folder still works), and a hidden helper folder (like `.thumbs`) or a generated `*.trickplay` folder sitting next to it does not disqualify it. A folder with an episode token in the filename, with season subfolders, or with more than one video is **not** a movie.

### Season folders

A folder is recognized as a season when its name matches (case-insensitive):

```text
Season 1   Season 01   Season  1   S1   S01   S001
```

Use `Season 0` or `S00` for specials. The series is the folder **above** the season folder. (A `Specials` folder named with the literal word is not auto-detected as a season — name it `Season 0`/`S00`, or use `S00Exx` tokens on the files.)

### Filename episode tokens

Independently of folders, the scanner reads an `SxxExx`-style token from the filename to assign season and episode numbers:

| Filename | Season | Episode |
| --- | --- | --- |
| `Show.S01E03.mkv` | 1 | 3 |
| `Show - s1e2.mkv` | 1 | 2 |
| `Show S01.E10.mkv` | 1 | 10 |
| `Dragon Ball Super (S1E1).mkv` | 1 | 1 |
| `[S02E05] Title.mkv` | 2 | 5 |

The separator before the token can be a space, `.`, `_`, `-`, `(`, or `[`. Other conventions — `1x03`, absolute episode numbers like `Show - 053`, or "Season 1 Episode 2" — are **not** parsed by the scanner; bring those in via provider [Identify](../using/identify.md) or by adding `SxxExx` tokens / season folders.

### Folder series fallback

A sub-folder under the root that holds **two or more** videos with no season folders and no episode tokens becomes a series named after the folder, with each video an episode ordered alphabetically by filename. (A single video in such a folder is a movie instead.)

## Sidecar metadata

Beside a video, the scanner imports metadata from:

| File | Contents |
| --- | --- |
| `<name>.nfo` or `movie.nfo` | `title`, `plot`, dates (`aired`/`premiered`/`releasedate`/`date`/`year`), `studio`, `url`, `tag`/`genre`, `actor`/`director`/`writer`, `runtime`/`duration`. |
| `<name>.info.json`, `<name>.json`, `movie.info.json`, `movie.json` | `title`/`fulltitle`, `description`/`plot`/`synopsis`, dates, channel/uploader/studio, `webpage_url`/`url`, `tags`/`categories`, `performers`/`actors`/`cast`, `duration`. |

Sidecar values seed the entity at scan time; your in-app edits and provider identify take precedence over routine rescans.

## Subtitles

Subtitle tracks are extracted from the video's embedded streams (and managed/added in the player). See [Playback & Reading](../using/playback.md#subtitles).

## Tips

- Put each movie in its **own folder** under the root so it lands in Movies; loose files at the root stay in Videos.
- For TV, prefer `Series/Season NN/SxxEyy.ext` for unambiguous results.
- If a show is misclassified, re-read the rules above, fix the layout, and rescan — classification is idempotent on file paths.
