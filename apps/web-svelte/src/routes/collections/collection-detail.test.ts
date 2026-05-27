import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("collection detail route", () => {
  it("uses ordered collection item membership for grids", async () => {
    const source = await readFile("src/routes/collections/[id]/+page.svelte", "utf8");

    expect(source).toContain("fetchCollectionItems");
    expect(source).toContain("<EntityGrid");
  });

  it("owns create and edit routes instead of linking to a missing shell fallback", async () => {
    const indexSource = await readFile("src/routes/collections/+page.svelte", "utf8");
    const newRoute = await readFile("src/routes/collections/new/+page.svelte", "utf8");
    const editRoute = await readFile("src/routes/collections/[id]/edit/+page.svelte", "utf8");

    expect(indexSource).toContain('actionHref="/collections/new"');
    expect(newRoute).toContain("CollectionEditor");
    expect(newRoute).toContain("isNew");
    expect(editRoute).toContain("CollectionEditor");
    expect(editRoute).toContain("getCollection");
  });

  it("wires manual curation and collection-specific writes through the collection API", async () => {
    const detailSource = await readFile("src/routes/collections/[id]/+page.svelte", "utf8");
    const apiSource = await readFile("src/lib/api/collections.ts", "utf8");
    const editorSource = await readFile("src/lib/components/collections/CollectionEditor.svelte", "utf8");
    const modelsSource = await readFile("src/lib/collections/models.ts", "utf8");
    const conditionBuilderSource = await readFile("src/lib/components/collections/ConditionBuilder.svelte", "utf8");

    expect(apiSource).toContain("createCollection");
    expect(apiSource).toContain("[201]");
    expect(apiSource).toContain("updateCollection");
    expect(apiSource).toContain("addCollectionItems");
    expect(apiSource).toContain("reorderCollectionItems");
    expect(apiSource).toContain("previewCollectionRules");
    expect(detailSource).toContain("EntityPicker");
    expect(detailSource).toContain("removeCollectionItems");
    expect(detailSource).toContain("refreshCollection");
    expect(editorSource).toContain("ConditionBuilder");
    expect(modelsSource).toContain("\"video-series\"");
    expect(conditionBuilderSource).toContain("value: \"video-series\"");
  });
});
