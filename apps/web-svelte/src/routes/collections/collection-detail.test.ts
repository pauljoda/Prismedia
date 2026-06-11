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
    const addMenuSource = await readFile("src/lib/components/entities/AddToCollectionMenu.svelte", "utf8");
    const editorSource = await readFile("src/lib/components/collections/CollectionEditor.svelte", "utf8");
    const modelsSource = await readFile("src/lib/collections/models.ts", "utf8");
    const conditionBuilderSource = await readFile("src/lib/components/collections/ConditionBuilder.svelte", "utf8");

    expect(apiSource).toContain("createCollection");
    expect(apiSource).toContain("[201]");
    expect(apiSource).toContain("updateCollection");
    expect(apiSource).toContain("addCollectionItems");
    expect(apiSource).toContain("reorderCollectionItems");
    expect(apiSource).toContain("previewCollectionRules");
    // Manual curation now happens via the Add to Collection grid bulk action,
    // which lists existing collections and adds the selection through the API.
    expect(addMenuSource).toContain("fetchCollections");
    expect(addMenuSource).toContain("addCollectionItems");
    expect(detailSource).toContain("refreshCollection");
    expect(editorSource).toContain("ConditionBuilder");
    expect(modelsSource).toContain("\"video-series\"");
    expect(conditionBuilderSource).toContain("value: \"video-series\"");
  });

  it("keeps collection detail metadata focused and prevents collections from nesting in rules", async () => {
    const detailSource = await readFile("src/routes/collections/[id]/+page.svelte", "utf8");
    const conditionBuilderSource = await readFile("src/lib/components/collections/ConditionBuilder.svelte", "utf8");
    const modelsSource = await readFile("src/lib/collections/models.ts", "utf8");
    const contractsSource = await readFile("../../packages/contracts/src/collections.ts", "utf8");

    expect(detailSource).toContain("standaloneMetadataSectionIds={[]}");
    expect(detailSource).toContain("Edit collection rules");
    expect(detailSource).toContain("icon: SlidersHorizontal");
    expect(detailSource).not.toContain("value: \"collection\"");
    expect(conditionBuilderSource).not.toContain("value: \"collection\"");
    expect(modelsSource).not.toContain("\"collection\"");
    expect(contractsSource).not.toContain("\"collection\"");
  });

  it("keeps collection editor settings focused on cover and mode controls", async () => {
    const editorSource = await readFile("src/lib/components/collections/CollectionEditor.svelte", "utf8");

    expect(editorSource).toContain("Collection Mode");
    expect(editorSource).toContain("Cover");
    expect(editorSource).toContain('label: "Standard"');
    expect(editorSource).not.toContain("Mark collection as NSFW");
    expect(editorSource).not.toContain("> Visibility");
    expect(editorSource.indexOf("Collection Mode")).toBeLessThan(editorSource.indexOf("Rule Editor"));
  });

  it("keeps the collection rule entity selector reachable on narrow screens", async () => {
    const conditionBuilderSource = await readFile(
      "src/lib/components/collections/ConditionBuilder.svelte",
      "utf8",
    );

    expect(conditionBuilderSource).toContain("Apply to");
    expect(conditionBuilderSource).toContain("overflow-x-auto");
    expect(conditionBuilderSource).toContain("[-webkit-overflow-scrolling:touch]");
    expect(conditionBuilderSource).toContain("inline-flex shrink-0 items-center");
    expect(conditionBuilderSource).not.toContain('style="padding-left: 3.5rem"');
  });

  it("derives collection hero artwork from member thumbnails when no custom cover exists", async () => {
    const detailSource = await readFile("src/routes/collections/[id]/+page.svelte", "utf8");

    expect(detailSource).toContain("collectionPosterCard");
    expect(detailSource).toContain("detailCard.posterCard?.cover");
    expect(detailSource).toContain("cardsWithCovers");
    expect(detailSource).toContain('collection?.coverMode === "item"');
    expect(detailSource).toContain("kind: THUMBNAIL_HOVER_KIND.imageSequence");
  });
});
