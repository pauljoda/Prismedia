# Library Organization

The authoritative, per-entity-type guide to how Prismedia turns your folder
layout into media now lives in the documentation site under **Library &
Scanning**, so there is a single source of truth that ships with the docs:

- [How Scanning Works](https://pauljoda.github.io/Prismedia/docs/library/overview) —
  watched roots, what the scanner skips, exclusions, incremental scans, sidecars.
- [Videos, Movies & Series](https://pauljoda.github.io/Prismedia/docs/library/videos) —
  movie/standalone/episode classification, season folders, filename tokens, sidecars.
- [Images & Galleries](https://pauljoda.github.io/Prismedia/docs/library/images-galleries)
- [Books, Comics & eBooks](https://pauljoda.github.io/Prismedia/docs/library/books) —
  `.cbz`/`.zip` comics and `.epub`/`.pdf` single-file books.
- [Audio & Music](https://pauljoda.github.io/Prismedia/docs/library/audio) —
  `Album/Songs` and `Artist/Album/Songs` layouts, disc sections, artists.

The page sources are in `documentation-site/docs/library/`. Update those when
scanner behavior changes; this file is only a pointer.
