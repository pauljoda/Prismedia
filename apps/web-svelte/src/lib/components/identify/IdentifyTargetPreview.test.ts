import { render, within } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import type { EntityCard } from "$lib/api/entities";
import { ENTITY_KIND, type EntityKindCode } from "$lib/entities/entity-codes";
import IdentifyTargetPreview from "./IdentifyTargetPreview.svelte";

describe("IdentifyTargetPreview", () => {
  const previewKinds: Array<[string, EntityKindCode]> = [
    ["audio libraries", ENTITY_KIND.audioLibrary],
    ["people", ENTITY_KIND.person],
    ["studios", ENTITY_KIND.studio],
    ["tags", ENTITY_KIND.tag],
    ["collections", ENTITY_KIND.collection],
  ];

  it.each(previewKinds)("renders the collapsed target preview for %s", (_label, kind) => {
    const { container } = render(IdentifyTargetPreview, {
      props: {
        entity: entity({ kind }),
      },
    });

    const previewToggle = within(container).getByRole("button", { name: /To Identify/ });
    expect(previewToggle).toBeInTheDocument();
    expect(previewToggle).toHaveAttribute("aria-expanded", "false");
  });
});

function entity(overrides: Partial<EntityCard> & { kind: EntityKindCode }): EntityCard {
  const { kind, ...rest } = overrides;

  return {
    id: "entity-1",
    kind,
    title: "Endgame",
    parentEntityId: null,
    sortOrder: null,
    coverUrl: null,
    coverThumbUrl: null,
    hoverKind: THUMBNAIL_HOVER_KIND.none,
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: false,
    ...rest,
  };
}
