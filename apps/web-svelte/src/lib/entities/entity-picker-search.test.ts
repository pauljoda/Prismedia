import { beforeEach, describe, expect, it, vi } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import { searchEntityPickerItems, searchPeople, searchStudios, searchTags } from "./entity-picker-search";

const mocks = vi.hoisted(() => ({ list: vi.fn() }));
vi.mock("$lib/api/generated/prismedia", () => ({ listEntities: mocks.list }));

describe("shared Entity picker search", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.list.mockResolvedValue({ status: 200, data: { items: [] } });
  });

  it("preserves API ordering and prefers small artwork", async () => {
    mocks.list.mockResolvedValue({ status: 200, data: { items: [
      { id: "second", title: "Second", coverThumbUrl: "/thumb.jpg", coverUrl: "/large.jpg", subtitle: "Caption" },
      { id: "first", title: "First", coverUrl: "/fallback.jpg" },
    ] } });
    expect(await searchEntityPickerItems(ENTITY_KIND.videoSeries, "series", { hideNsfw: true })).toEqual([
      { id: "second", title: "Second", thumbnailUrl: "/thumb.jpg", subtitle: "Caption" },
      { id: "first", title: "First", thumbnailUrl: "/fallback.jpg", subtitle: undefined },
    ]);
    expect(mocks.list).toHaveBeenCalledWith({ kind: ENTITY_KIND.videoSeries, query: "series", limit: 20, hideNsfw: true });
  });

  it("keeps detail picker requests unchanged", async () => {
    await searchTags("");
    await searchPeople("name");
    await searchStudios("studio");
    expect(mocks.list.mock.calls.map(([params]) => params.kind)).toEqual([ENTITY_KIND.tag, ENTITY_KIND.person, ENTITY_KIND.studio]);
    expect(mocks.list.mock.calls[0][0]).toEqual({ kind: ENTITY_KIND.tag, query: undefined, limit: 20 });
  });

  it("surfaces server errors to the shared picker retry state", async () => {
    mocks.list.mockResolvedValue({ status: 503, data: { detail: "Unavailable" } });
    await expect(searchTags("test")).rejects.toThrow("Unavailable");
  });
});
