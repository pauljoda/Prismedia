import { beforeEach, describe, expect, it, vi } from "vitest";

const generated = vi.hoisted(() => ({
  getAcquisitionForEntity: vi.fn(),
  listAcquisitionsForEntity: vi.fn(),
}));

vi.mock("$lib/api/generated/prismedia", () => generated);

import { fetchAcquisitionForEntity, fetchAcquisitionsForEntity } from "./acquisitions";

describe("acquisition API", () => {
  beforeEach(() => vi.clearAllMocks());

  it("returns null when an entity has no acquisition", async () => {
    generated.getAcquisitionForEntity.mockResolvedValue({
      status: 204,
      data: undefined,
    });

    await expect(fetchAcquisitionForEntity("movie-1")).resolves.toBeNull();
  });

  it("loads every parallel acquisition for one Book entity", async () => {
    generated.listAcquisitionsForEntity.mockResolvedValue({
      status: 200,
      data: [{ summary: { id: "ebook-acquisition" } }, { summary: { id: "audiobook-acquisition" } }],
    });

    await expect(fetchAcquisitionsForEntity("book-1")).resolves.toEqual([
      { summary: { id: "ebook-acquisition" } },
      { summary: { id: "audiobook-acquisition" } },
    ]);
    expect(generated.listAcquisitionsForEntity).toHaveBeenCalledWith("book-1");
  });
});
