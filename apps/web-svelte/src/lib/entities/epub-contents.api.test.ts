import { describe, expect, it } from "vitest";
import type { BookContentsEntry } from "$lib/api/generated/model";
import { mapBookContentsEntries, resolveCurrentContentsEntry } from "./epub-contents";

const items: BookContentsEntry[] = [
  {
    id: "one.xhtml",
    title: "One",
    location: "one.xhtml",
    depth: "0" as unknown as number,
    order: "0" as unknown as number,
    sectionIndex: "0" as unknown as number,
    startFraction: 0.1,
    endFraction: 0.4,
  },
  {
    id: "two.xhtml",
    title: "Two",
    location: "two.xhtml",
    depth: 0,
    order: 1,
    sectionIndex: null,
    startFraction: null,
    endFraction: null,
  },
  {
    id: "three.xhtml",
    title: "Three",
    location: "three.xhtml",
    depth: 0,
    order: 2,
    sectionIndex: 2,
    startFraction: 0.4,
    endFraction: 1,
  },
];

describe("EPUB contents normalization", () => {
  it("normalizes the compact server contract", () => {
    const entries = mapBookContentsEntries(items);

    expect(entries[0]).toMatchObject({ depth: 0, sectionIndex: 0, startFraction: 0.1 });
    expect(entries[1]).toMatchObject({ sectionIndex: null, startFraction: null });
  });

  it("resolves the current chapter by fraction when the cursor is a CFI", () => {
    const entries = mapBookContentsEntries(items);

    const current = resolveCurrentContentsEntry(entries, "epubcfi(/6/14!/4/2)", 0.65);

    expect(current?.id).toBe("three.xhtml");
  });

  it("prefers an exact location match over the fraction", () => {
    const entries = mapBookContentsEntries(items);

    const current = resolveCurrentContentsEntry(entries, "one.xhtml", 0.65);

    expect(current?.id).toBe("one.xhtml");
  });
});
