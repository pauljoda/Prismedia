import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const source = readFileSync("src/routes/books/[id]/+page.svelte", "utf8");

describe("Book detail audiobook experience", () => {
  it("keeps reading and listening as independent shared actions and progress panels", () => {
    expect(source).toContain('id: "read-book"');
    expect(source).toContain('id: "listen-book"');
    expect(source).toContain('<MediaProgressPanel\n          kind="read"');
    expect(source).toContain('<MediaProgressPanel\n          kind="listen"');
    expect(source).toContain("Continue listening");
  });

  it("queues ordered audio-track children with the Book as playback owner", () => {
    expect(source).toContain("audiobookTrackItems(book)");
    expect(source).toContain("resolveAudiobookResume(");
    expect(source).toContain("playbackOwnerEntityId: book.id");
    expect(source).toContain("playbackOwnerEntityKind: ENTITY_KIND.book");
    expect(source).toContain("startSeconds: resume.trackOffsetSeconds");
  });

  it("updates listening completion without reporting total runtime as watched-duration delta", () => {
    const toggleStart = source.indexOf("async function handleToggleListened");
    const toggleEnd = source.indexOf("async function startListeningOver", toggleStart);
    const startOverEnd = source.indexOf("function resumeProgress", toggleEnd);
    const mutationSource = source.slice(toggleStart, startOverEnd);

    expect(mutationSource).toContain("updateEntityPlayback(book.id");
    expect(mutationSource).not.toContain("durationSeconds");
  });

  it("always exposes first-class ebook and audiobook management on the Book acquisition tab", () => {
    expect(source).toContain("<BookRenditionAcquisitionCard");
    expect(source).toContain("ebook: hasReadableContent");
    expect(source).toContain("audiobook: audiobookTracks.length > 0");
    expect(source).toContain("fetchAcquisitionsForEntity(targetBookId)");
    expect(source).toContain("fetchEntityMonitors(targetBookId)");
    expect(source).toContain("commitEntityRequest(book.id, rendition)");
    expect(source).not.toContain('...(acq.visible\n        ? [{ id: "acquisition"');
  });
});
