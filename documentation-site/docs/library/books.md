---
sidebar_position: 4
title: Books, Comics & eBooks
description: How comic archives and single-file eBooks are scanned, structured, and read.
---

# Books, Comics & eBooks

When a watched root has **Books** enabled, the scanner handles two distinct kinds of files:

- **Comic archives** — `.cbz` and `.zip` — whose pages and chapters live as image files inside the archive.
- **Single-file books (eBooks)** — `.epub` and `.pdf` — where the whole book is one self-contained file.

```text
Comic archives:   .cbz  .zip
Single-file books: .epub  .pdf
```

These share the **Books** library but show up under focused sidebar sections:

| Sidebar section | Shows |
| --- | --- |
| **Books** | Everything (comics, manga, books, novels). |
| **Comics** | Book types `comic` and `manga`. |
| **eBooks** | Book types `book` and `novel`, limited to EPUB/PDF formats. |

Book **type** (Book / Comic / Manga / Novel) and **format** (Comic Archive / EPUB / PDF) are also filterable from the library filter drawer. EPUB defaults to type *Novel*; PDF defaults to type *Book*; comic archives are *Comic*/*Manga* (with `ComicInfo.xml`'s `Manga` flag taken into account).

## Comic archives (`.cbz` / `.zip`)

A comic archive is opened and its image members become **pages**. Recognized page images inside the archive:

```text
.jpg  .jpeg  .png  .gif  .webp  .bmp  .tiff  .tif
```

### Folder layout → entity

Folder depth below the root determines the Book → Volume → Chapter structure:

```text
/media/comics/
├── One Shot.cbz                              → Book "One Shot"  (single archive)
│
├── Saga/
│   ├── Chapter 001.cbz                       → Book "Saga" › Chapter "Chapter 001"
│   └── Chapter 002.cbz                       → Book "Saga" › Chapter "Chapter 002"
│
└── Berserk/
    ├── Volume 01/
    │   ├── Chapter 001.cbz                    → Book "Berserk" › Volume "Volume 01" › Chapter
    │   └── Chapter 002.cbz                    → Book "Berserk" › Volume "Volume 01" › Chapter
    └── Volume 02/
        └── Chapter 003.cbz                    → Book "Berserk" › Volume "Volume 02" › Chapter
```

| Layout | Becomes |
| --- | --- |
| `book.cbz` at the root | A standalone single book. |
| `Series/Chapter.cbz` | Book named after the folder, with each archive a chapter. |
| `Series/Volume NN/Chapter.cbz` | Book → Volume → Chapter. |

### `ComicInfo.xml`

A `ComicInfo.xml` at the root of the archive enriches the entity:

| Element(s) | Used for |
| --- | --- |
| `Title`, `Series` | Chapter/book title and series grouping (Series can override the folder name). |
| `Number`, `Count`, `Volume` | Issue/chapter number, total, and volume. |
| `Summary` | Description. |
| `Year` / `Month` / `Day` | Normalized date. |
| `Publisher` / `Imprint` | Studio/publisher. |
| `Web` | Links. |
| `PageCount` | Page count. |
| `LanguageISO` | Language. |
| `Writer`, `Penciller`, `Inker`, `Colorist`, `Letterer`, `CoverArtist`, `Editor`, `Translator` | People/creators (split on `;`/`,`). |
| `Genre`, `Tags`, `Characters`, `SeriesGroup`, `StoryArc`, `Manga`, `AgeRating` | Tags. |
| `AgeRating` | Flags the book NSFW when it reads as adult/mature/explicit/18+/etc. |

## Single-file books (`.epub` / `.pdf`)

Each `.epub` or `.pdf` scans into **one book entity** — there are no separate chapter or page entities, because the structure lives inside the file. Title, author, and description are read straight from the file:

| Format | Metadata source | Cover |
| --- | --- | --- |
| EPUB | VersOne.Epub (title, authors, description) | Embedded cover image. |
| PDF | PdfPig (title, author, subject) | Rendered from the first page. |

### Folder layout → entity

```text
/media/ebooks/
├── Dune.epub                         → eBook "Dune"  (standalone at root)
├── The Hobbit.pdf                    → eBook "The Hobbit"  (standalone at root)
│
└── Mistborn/
    ├── The Final Empire.epub         → Series "Mistborn" › book
    ├── The Well of Ascension.epub    → Series "Mistborn" › book
    └── The Hero of Ages.epub         → Series "Mistborn" › book
```

- A root-level `.epub`/`.pdf` is a standalone book.
- `.epub`/`.pdf` files inside a folder are grouped into a folder-backed **book series**: the Books library shows the folder once, and its detail page opens into the books inside.

## Reading

Books open in a focused, full-page reader route:

| Format | Reader | Modes |
| --- | --- | --- |
| Comic archive | Comic reader | Paged (tap zones, swipe, arrows) and vertical **webtoon** scroll; single or two-page spreads with cover handling. |
| EPUB | foliate-js reflowable reader | Paged or scrolled, adjustable text size, comic-style tap zones; resumes by CFI. |
| PDF | pdf.js reader | Continuous scroll with selectable text, zoom (fit-width / fit-page / +/- / pinch), gapless toggle, in-document search, working links, download, outline popup; plus a paged mode. Resumes by page. |

Every book remembers where you left off, shows a reading-progress panel (status, percentage, position, Resume / Start Over, read/unread toggle), and a progress bar along its cover in grids. See [Playback & Reading](../using/playback.md#book-and-comic-reader).
