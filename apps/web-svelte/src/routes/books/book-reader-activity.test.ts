import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const source = readFileSync("src/routes/books/[id]/reader/+page.svelte", "utf8");

describe("Book reader activity", () => {
  it("reports active reading time through periodic canonical progress heartbeats", () => {
    expect(source).toContain("const readerActivityClock = new BookActivityClock();");
    expect(source).toContain("queueReaderActivityHeartbeat(false)");
    expect(source).toContain("queueReaderActivityHeartbeat(true)");
    expect(source).toContain("activityKind: activitySeconds ? BOOK_ACTIVITY_KIND.reading : undefined");
    expect(source).toContain("activitySeconds,");
  });

  it("uses an estimated whole-book fraction when listening produced no exact EPUB CFI", () => {
    expect(source).toContain("singleFileInitialFraction = launchFraction");
    expect(source).toContain("Number(progress?.index ?? 0) / Number(progress?.total ?? 0)");
  });
});
