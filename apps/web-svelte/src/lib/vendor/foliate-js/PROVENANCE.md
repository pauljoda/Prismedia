# foliate-js (vendored)

Reflowable/fixed book rendering engine used by Prismedia's book reader for EPUB
(and, in Phase 2, PDF). Vendored rather than installed because foliate-js is not
published to npm.

- Source: https://github.com/johnfactotum/foliate-js
- Pinned commit: `78914aef4466eb960965702401634c2cb348e9b1`
- License: MIT (see `LICENSE`)

Embedding entry point is `view.js`, which defines the `<foliate-view>` custom
element. `vendor/` holds foliate's own bundled dependencies (zip.js, fflate, and
pdf.js/cmaps/standard_fonts under `vendor/pdfjs`). foliate renders both EPUB and PDF,
so Prismedia's single book reader uses it for both. `pdf.js` and `vendor/pdfjs` load
lazily and only when a PDF is opened.

Local modifications (re-apply when updating the pinned commit):

- `pdf.js`: the two layer-CSS values are imported at build time via `?raw` (instead of a
  module-level top-level `await fetchText`, which our build target rejects), and
  `pdfjsPath` returns a plain absolute URL under `/foliate-pdfjs/` rather than using
  `new URL(..., import.meta.url)` — Vite rewrites a dynamic `new URL` into a glob lookup
  that returns `undefined` for the `cmaps/`/`standard_fonts/` directories.
- `vendor/pdfjs`: only the bundled bits live here now — `pdf.mjs` (statically imported) and
  the two layer CSS files (imported `?raw`). The runtime assets fetched by pdf.js at run time
  (`pdf.worker.mjs`, `cmaps/`, `standard_fonts/`) live in `static/foliate-pdfjs/` so they are
  served at a stable URL. The `*.map` source maps (~7.7MB) were omitted.

Dev/build files (`reader.js`, `reader.html`, `tests/`, `rollup/`, config) were not
vendored. Update by re-copying the runtime modules from a newer pinned commit.
