# foliate-js (vendored)

Reflowable/fixed book rendering engine used by Prismedia's book reader for EPUB
(and, in Phase 2, PDF). Vendored rather than installed because foliate-js is not
published to npm.

- Source: https://github.com/johnfactotum/foliate-js
- Pinned commit: `78914aef4466eb960965702401634c2cb348e9b1`
- License: MIT (see `LICENSE`)

Embedding entry point is `view.js`, which defines the `<foliate-view>` custom
element. `vendor/` holds foliate's own bundled dependencies (zip.js, fflate).

Local modifications (re-apply when updating the pinned commit):

- `pdf.js` is **stubbed**. Prismedia uses foliate only for reflowable EPUB; PDFs are
  rendered by a dedicated pdfjs-dist reader. The upstream `pdf.js` pulled in a ~13MB
  vendored pdfjs build and used top-level await (unsupported by our build target), so
  `vendor/pdfjs/` was deleted and `pdf.js` replaced with a throwing stub that keeps
  `makeBook()`'s dynamic import resolvable.

Dev/build files (`reader.js`, `reader.html`, `tests/`, `rollup/`, config) were not
vendored. Update by re-copying the runtime modules from a newer pinned commit.
