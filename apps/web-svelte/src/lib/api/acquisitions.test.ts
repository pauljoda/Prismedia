import { beforeEach, describe, expect, it, vi } from "vitest";

const generated = vi.hoisted(() => ({
  listAcquisitionsForEntity: vi.fn(),
}));

vi.mock("$lib/api/generated/prismedia", () => generated);

import { fetchAcquisitionsForEntity } from "./acquisitions";

describe("acquisition API", () => {
  beforeEach(() => vi.clearAllMocks());

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
