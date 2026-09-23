import { beforeEach, describe, expect, it, vi } from "vitest";
import { BOOK_RENDITION } from "$lib/api/generated/codes";

const generated = vi.hoisted(() => ({
  commitBookRenditionsRequest: vi.fn(),
  commitEntityRequest: vi.fn(),
  removeWanted: vi.fn(),
}));

vi.mock("$lib/api/generated/prismedia", () => generated);

import { commitBookRenditionsRequest, commitEntityRequest, removeWantedEntities } from "./requests";

describe("request API", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("returns the complete server-confirmed wanted removal result", async () => {
    generated.removeWanted.mockResolvedValue({
      status: 200,
      data: { removed: "1", failures: [] },
    });

    await expect(removeWantedEntities(["wanted-1"])).resolves.toEqual({
      removed: "1",
      failures: [],
    });
    expect(generated.removeWanted).toHaveBeenCalledWith({ entityIds: ["wanted-1"] });
  });

  it("targets the exact missing Book rendition on the existing entity", async () => {
    generated.commitEntityRequest.mockResolvedValue({
      status: 200,
      data: { items: [] },
    });

    await commitEntityRequest("book-1", BOOK_RENDITION.audiobook);

    expect(generated.commitEntityRequest).toHaveBeenCalledWith({
      entityId: "book-1",
      bookRendition: BOOK_RENDITION.audiobook,
    });
  });

  it("passes both Book formats through one typed request", async () => {
    const result = { items: [], bookRenditions: [] };
    generated.commitBookRenditionsRequest.mockResolvedValue({ status: 200, data: result });
    await expect(commitBookRenditionsRequest("book-1", [
      { rendition: BOOK_RENDITION.ebook },
      { rendition: BOOK_RENDITION.audiobook },
    ])).resolves.toEqual(result);
    expect(generated.commitBookRenditionsRequest).toHaveBeenCalledWith({
      entityId: "book-1",
      renditions: [
        { rendition: BOOK_RENDITION.ebook },
        { rendition: BOOK_RENDITION.audiobook },
      ],
    });
  });

  it("preserves structured cleanup failures returned with HTTP 200", async () => {
    generated.removeWanted.mockResolvedValue({
      status: 200,
      data: {
        removed: 0,
        failures: [{ entityId: "wanted-1", message: "The transfer could not be removed." }],
      },
    });

    await expect(removeWantedEntities(["wanted-1"])).resolves.toEqual({
      removed: 0,
      failures: [{ entityId: "wanted-1", message: "The transfer could not be removed." }],
    });
  });
});
