---
sidebar_position: 3
title: Images & Galleries
description: How image files become loose images and nested galleries.
---

# Images & Galleries

When a watched root has **Images** enabled, the scanner groups image files by folder: loose files at the root stay standalone, and folders of images become galleries.

## Supported image extensions

Still images:

```text
.jpg  .jpeg  .png  .gif  .webp  .bmp  .tiff  .tif  .heic  .avif  .svg  .ico  .tga  .psd
```

Galleries may also contain **web-style animated clips**, which are treated as gallery items rather than standalone videos:

```text
.webm  .mp4  .m4v  .mkv  .mov  .avi  .wmv  .flv
```

Animated items play inline in the gallery feed and in the lightbox.

## Loose images vs galleries

- An image **directly at the root** stays a standalone **Image**.
- A **folder** of images becomes a **Gallery** containing those images.
- Folders nest: a gallery inside a gallery folder becomes a child gallery.

## Folder layout → entity

```text
/media/images/
├── sunset.jpg                       → Image  (loose at root)
├── skyline.png                      → Image  (loose at root)
│
├── Iceland 2023/
│   ├── 001.jpg                      → Gallery "Iceland 2023" › image
│   ├── 002.jpg                      → Gallery "Iceland 2023" › image
│   ├── clip.mp4                     → Gallery "Iceland 2023" › animated item
│   └── Day Two/
│       ├── 010.jpg                  → child Gallery "Day Two" › image
│       └── 011.jpg                  → child Gallery "Day Two" › image
│
└── One Off/
    └── single.jpg                   → Image (loose) — the folder is collapsed
```

## Single-image folders are collapsed

If a folder holds **just one image** and nothing else, Prismedia does **not** create a one-item gallery. Instead the image is shown directly under the nearest parent gallery, or as a loose image when there is no parent. This avoids the single-image "gallery" artifact some downloaders produce. Existing libraries clean themselves up on the next scan.

## Browsing

Galleries and images browse as **Grid**, **List**, or **Feed** (a single full-width column where each item shows at its real shape and animated items play inline). Tapping an item opens the universal lightbox. See [Browsing the Library](../using/browsing.md) and [Playback & Reading](../using/playback.md#image-lightbox).

## Metadata

Images and galleries carry ratings, tags, people, studios, links, and artwork that you set in the app or apply through [Identify](../using/identify.md). There is no image sidecar format; metadata comes from your edits and providers.
