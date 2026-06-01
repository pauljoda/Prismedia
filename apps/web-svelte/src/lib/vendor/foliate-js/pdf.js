// Prismedia stub. foliate-js is used here only for reflowable formats (EPUB);
// PDF files are rendered by a dedicated pdfjs-dist reader instead. The upstream
// foliate pdf.js pulled in a ~13MB vendored pdfjs build and used top-level await,
// which we don't need. This stub keeps makeBook()'s dynamic import resolvable.
// See PROVENANCE.md.
export const makePDF = async () => {
  throw new Error("PDF rendering is handled by the dedicated PDF reader, not foliate-js");
};
