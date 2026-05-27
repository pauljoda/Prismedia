import { describe, expect, it } from "vitest";
import { getListEntitiesUrl } from "$lib/api/generated/prismedia";

describe("generated entity API", () => {
  it("includes typed list query params used by generated-client consumers", () => {
    const url = getListEntitiesUrl({
      kind: "video",
      query: "matrix",
      hideNsfw: true,
      limit: 25,
    });

    expect(url).toContain("/api/entities?");
    expect(url).toContain("kind=video");
    expect(url).toContain("query=matrix");
    expect(url).toContain("hideNsfw=true");
    expect(url).toContain("limit=25");
  });
});
