---
sidebar_position: 5
title: Audio & Music
description: How audio files become artists, albums, disc sections, and tracks.
---

# Audio & Music

When a watched root has **Audio** enabled, the scanner reads your music folders into artists, albums, and tracks. It enforces two stable folder layouts rather than turning every folder level into a nested album.

## Supported audio extensions

```text
.mp3  .flac  .wav  .ogg  .aac  .m4a  .wma  .opus  .aiff  .aif  .alac  .ape  .dsf  .dff  .wv
```

## The two layouts

### `Album/Songs`

A folder of tracks is an **album**:

```text
/media/music/
├── Random Access Memories/
│   ├── 01 Give Life Back to Music.flac     → Album "Random Access Memories" › track 1
│   └── 02 The Game of Love.flac            → Album "Random Access Memories" › track 2
```

### `Artist/Album/Songs`

A folder of album folders is an **artist**, and each child folder is an album:

```text
/media/music/
└── Daft Punk/                               → Artist "Daft Punk"
    ├── Discovery/
    │   ├── 01 One More Time.flac            → Album "Discovery" › track 1
    │   └── 02 Aerodynamic.flac              → Album "Discovery" › track 2
    └── Homework/
        └── 01 Daftendirekt.flac             → Album "Homework" › track 1
```

Folders are classified **leaf-first**: a folder with direct tracks (or only disc-section subfolders) is an album; a folder whose children are albums is an artist. Loose tracks sitting at the root stay standalone.

## Disc sections

A disc subfolder inside an album becomes a **section** of that one album, with track numbering that restarts per disc, so multi-disc and box sets stay together as a single album. Recognized section names include:

```text
Disc 1   Disk 2   CD2   Side A   Vol. 3   Volume II   Part 1   Disque 1
```

```text
/media/music/
└── The Beatles/
    └── The White Album/                     → Album "The White Album"
        ├── Disc 1/
        │   ├── 01 Back in the U.S.S.R..flac → section "Disc 1" › track 1
        │   └── 02 Dear Prudence.flac        → section "Disc 1" › track 2
        └── Disc 2/
            └── 01 Birthday.flac             → section "Disc 2" › track 1
```

## Artists and members

An **Artist** is a first-class grouping (like a gallery is for images) with its own page, metadata, and **members** — people credited with a role such as Drummer or Vocals, modeled like a series cast. The **MusicBrainz** plugin can identify an artist (pulling members, origin, formed year, and genres) in addition to albums and tracks. See [Identify & Enrich](../getting-started/identify-walkthrough.md).

## Embedded tags and cover art

Track metadata — title, artist, album, track and disc numbers, and embedded cover art — is read from the file's tags at **probe time** (the job that runs after scanning). Identifying an album matches its tracks to the release's track list by track number (using the embedded track-number tag when present, falling back to file order) and can rename track titles to the canonical names; the file on disk is left untouched.

## Playback

Audio plays through a persistent player bar that keeps playing as you browse, with a queue and "Up Next" list, shuffle, waveform scrubbing, OS media-control integration, and resume/play-count tracking. See [Playback & Reading](../using/playback.md#audio-playback).
