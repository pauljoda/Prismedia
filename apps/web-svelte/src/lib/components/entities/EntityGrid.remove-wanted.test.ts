import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CAPABILITY_KIND, ENTITY_KIND, THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import EntityGrid from "./EntityGrid.test-harness.svelte";

const requests = vi.hoisted(() => ({
  removeWantedEntities: vi.fn(),
}));

vi.mock("$lib/api/requests", () => requests);

describe("EntityGrid wanted removal", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("hides only server-confirmed removals and leaves retained targets selected", async () => {
    requests.removeWantedEntities.mockImplementation(async ([entityId]: string[]) => {
      if (entityId === "wanted-1") return { removed: 1, failures: [] };
      return {
        removed: 0,
        failures: [{
          entityId,
          message: "The download client is unavailable. Restore it and retry.",
        }],
      };
    });
    const onSelectionChange = vi.fn();
    render(EntityGrid, {
      props: {
        cards: [wantedCard("wanted-1", "Wanted One"), wantedCard("wanted-2", "Wanted Two")],
        dockControls: false,
        initialSelectionActive: true,
        onSelectionChange,
        showPagination: false,
      },
    });

    await fireEvent.click(screen.getByLabelText("Select Wanted One"));
    await fireEvent.click(screen.getByLabelText("Select Wanted Two"));
    await fireEvent.click(screen.getByRole("button", { name: "Remove wanted" }));

    await waitFor(() => expect(requests.removeWantedEntities).toHaveBeenCalledTimes(2));
    expect(requests.removeWantedEntities.mock.calls).toEqual([
      [["wanted-1"]],
      [["wanted-2"]],
    ]);
    await waitFor(() => expect(screen.queryByLabelText("Select Wanted One")).not.toBeInTheDocument());
    expect(screen.getByLabelText("Select Wanted Two")).toBeChecked();
    expect(screen.getByRole("alert")).toHaveTextContent(
      "The download client is unavailable. Restore it and retry.",
    );
    expect(onSelectionChange).toHaveBeenLastCalledWith(["wanted-2"]);
  });
});

function wantedCard(id: string, title: string): EntityThumbnailCard {
  return {
    entity: {
      id,
      kind: ENTITY_KIND.book,
      title,
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [{
        kind: CAPABILITY_KIND.flags,
        isFavorite: null,
        isNsfw: null,
        isOrganized: null,
        isWanted: true,
      }],
      childrenByKind: [],
    },
    aspectRatio: "poster",
    cover: null,
    hover: { kind: THUMBNAIL_HOVER_KIND.none },
  };
}
