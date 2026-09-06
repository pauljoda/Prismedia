---
sidebar_position: 4
title: Books, Comics, eBooks & Audiobooks
description: How comics, eBooks, and audiobooks are scanned, structured, read, and played.
---

# Books, Comics, eBooks & Audiobooks

When a watched root has **Books** enabled, the scanner handles three distinct kinds of files:

- **Comic archives** — `.cbz` and `.zip` — whose pages and chapters live as image files inside the archive.
- **Single-file books (eBooks)** — `.epub` and `.pdf` — where the whole book is one self-contained file.
- **Audiobooks** — `.m4b`, `.m4a`, and `.mp3` — where one file or an ordered set of files becomes the listening rendition of a Book.

```text
Comic archives:   .cbz  .zip
Single-file books: .epub  .pdf
Audiobooks:         .m4b  .m4a  .mp3
```

Enable **Books** on the watched root for all three formats. Prose books and audiobooks share a Book item; serialized comics have their own series, optional volumes, and readable installments. A comic issue or released chapter is an installment, distinct from a prose book's internal chapter markers.

For a complete starting layout and root settings, see [Organize Your Media Folders](../getting-started/organize-folders.md).

## Comic archives (`.cbz` / `.zip`)

A comic archive is opened and its image members become **pages**. Recognized page images inside the archive:

```text
.jpg  .jpeg  .png  .gif  .webp  .bmp  .tiff  .tif
```

### Folder layout → entity

Folders below the root provide the comic series and optional volume. Each archive becomes one readable installment:

```text
/media/comics/                           ← watched root, Books enabled
├── One Shot.cbz                        → Series "One Shot" › one installment
├── Example Comic/
│   ├── Issue 001.cbz                   → Series "Example Comic" › installment
│   └── Issue 002.cbz                   → Series "Example Comic" › installment
└── Example Manga/
    ├── Volume 01/
    │   ├── Chapter 001.cbz             → Series › Volume 01 › installment
    │   └── Chapter 002.cbz             → Series › Volume 01 › installment
    └── Volume 02/
        └── Chapter 003.cbz             → Series › Volume 02 › installment
```

| Layout | Becomes |
| --- | --- |
| `One Shot.cbz` at the root | A comic series with one readable installment. |
| `Series/Issue.cbz` | A series with each archive as a direct installment. |
| `Series/Volume NN/Chapter.cbz` | A series with a volume containing installments. |

These examples assume no metadata overrides. `ComicInfo.xml` can supply the series name and volume number; a volume number can group installments even without a volume folder. Root-level archives with the same metadata series name can group together.

Pages are stored as an ordered manifest inside the installment, not as separate library items. Archives without safe readable pages are skipped. Explicitly bounded loose-page comic folders can also generate managed CBZ copies; Prismedia preserves their original page files. For a predictable starting layout, use one CBZ per issue or released chapter.

### `ComicInfo.xml`

A `ComicInfo.xml` at the root of the archive enriches the entity:

| Element(s) | Used for |
| --- | --- |
| `Title`, `Series` | Installment title and series grouping (Series can override the folder name). |
| `Number`, `Count`, `Volume` | Issue/chapter number, total, and volume. |
| `Summary` | Description. |
| `Year` / `Month` / `Day` | Normalized date. |
| `Publisher` / `Imprint` | Studio/publisher. |
| `Web` | Links. |
| `PageCount` | Page count. |
| `LanguageISO` | Language. |
| `Writer`, `Penciller`, `Inker`, `Colorist`, `Letterer`, `CoverArtist`, `Editor`, `Translator` | People/creators (split on `;`/`,`). |
| `Genre`, `Tags`, `Characters`, `SeriesGroup`, `StoryArc`, `Manga`, `AgeRating` | Tags. |
| `AgeRating` | Marks the comic restricted when it reads as adult/mature/explicit/18+/etc. |

## Single-file books (`.epub` / `.pdf`)

Each `.epub` or `.pdf` scans into **one book entity** — there are no separate chapter or page entities, because the structure lives inside the file. Title, author, and description are read straight from the file:

| Format | Metadata source | Cover |
| --- | --- | --- |
| EPUB | VersOne.Epub (title, authors, description) | Embedded cover image. |
| PDF | PdfPig (title, author, subject) | Rendered from the first page. |

### Folder layout → entity

```text
/media/books/                            ← watched root, Books enabled
├── Example Book.epub                   → Standalone book
└── Example Author/                     → Author grouping
    ├── First Book/
    │   └── First Book.epub             → Book under that author
    └── Second Book/
        └── Second Book.pdf             → Book under that author
```

- A root-level `.epub`/`.pdf` is a standalone book.
- For files in subfolders, the first folder beneath the watched root provides an **author grouping**. The displayed author name prefers embedded creator metadata and otherwise uses the folder name.
- A folder is not automatically a prose book series. Prefer `Author/Title/Book.epub` so the fallback names are meaningful, and use metadata to describe series relationships.

## Audiobooks (`.m4b` / `.m4a` / `.mp3`)

Audiobooks are discovered by the **Books** scanner, not by enabling the music-oriented **Audio** scanner. Add them anywhere beneath an enabled, recursive Books root such as `/media/books`. This is the path inside the container; if the host folder is mounted as `/srv/media:/media`, `/media/books` corresponds to `/srv/media/books` on the host.

Use a Books-only watched root for audiobook folders. If **Audio** is also enabled on the same root, the same files are eligible for music album/track classification.

### Recommended manual layout

Keep every multipart audiobook in its own title folder. To give one Book both readable and listening renditions, put the EPUB/PDF and audiobook parts in the same folder:

```text
/media/books/
└── Frank Herbert/
    └── Dune/
        ├── Dune.epub
        ├── 01 - Arrakis.m4b
        ├── 02 - Muad'Dib.mp3
        └── 03 - The Desert.m4a
```

An audio-only title uses the same one-folder-per-book rule:

```text
/media/books/
└── Project Hail Mary/
    └── Project Hail Mary.m4b
```

### Folder layout → entity

| Layout | Becomes |
| --- | --- |
| `Book Title.m4b` at the root | A standalone audio-only Book. |
| `Book Title/*.m4b` (or `.m4a`/`.mp3`) | One audio-only Book named after the folder; every audio file in that folder is one ordered part. |
| `Book Title/Book Title.epub` plus audio files | One Book with both reading and listening renditions. |

Audio parts are ordered naturally by their full paths, so zero-padded names such as `01`, `02`, and `03` give predictable playback order. Do not put multipart files from several books in one folder: every unmatched audio file in a subfolder is grouped by that containing folder.

When audio and readable files share a folder, the scanner attaches audio to the readable Book using these rules:

1. Prefer an EPUB/PDF with the same filename stem, such as `Dune.epub` and `Dune.m4b`.
2. Otherwise, attach to the readable Book when that folder contains exactly one EPUB/PDF.
3. If the folder contains multiple readable books and no filename matches, keep the audio as a separate audio-only Book rather than guessing.

A single audiobook file may live directly at the watched root, but every root-level audio file is treated as a separate Book. Put multipart audiobooks in a title folder.

After copying files outside Prismedia, run a scan from **Settings → Watched Libraries**, **Files**, or **Jobs**. The Book detail page shows the discovered parts in its Listening section.

## Reading

Books open in a focused, full-page reader route:

| Format | Reader | Modes |
| --- | --- | --- |
| Comic archive | Comic reader | Paged (tap zones, swipe, arrows) and vertical **webtoon** scroll; single or two-page spreads with cover handling. |
| EPUB | foliate-js reflowable reader | Paged or scrolled, adjustable text size, comic-style tap zones; resumes by CFI. |
| PDF | pdf.js reader | Continuous scroll with selectable text, zoom (fit-width / fit-page / +/- / pinch), gapless toggle, in-document search, working links, download, outline popup; plus a paged mode. Resumes by page. |

Every book remembers where you left off, shows a reading-progress panel (status, percentage, position, Resume / Start Over, read/unread toggle), and a progress bar along its cover in grids. See [Playback & Reading](../using/playback.md#book-and-comic-reader).

Books with an audiobook also show **Listen** or **Resume listening**. Multipart audio plays in filename order and stores listening progress independently from EPUB/PDF reading progress.
