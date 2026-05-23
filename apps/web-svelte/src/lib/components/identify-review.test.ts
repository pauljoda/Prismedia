import { describe, expect, it } from "vitest";
import type { EntityMetadataProposal } from "$lib/api/identify";
import {
  buildProposalForApply,
  findRelationshipImage,
  groupProposalRows,
  isNewRelationshipTitle,
  reviewChildProposals,
  relationshipProposals,
  relationshipTitlesFromEntityThumbnails,
  structuralChildProposals,
} from "./identify-review";

describe("identify review helpers", () => {
  it("separates structural children from related entity proposals", () => {
    const root = proposal("series", "video-series", {
      children: [
        proposal("season-1", "video-season"),
      ],
      relationships: [
        proposal("actor-1", "person", { title: "Series Actor", imageKind: "poster", imageUrl: "https://example.test/actor.jpg" }),
        proposal("studio-1", "studio", { title: "Chair Pictures", imageKind: "logo", imageUrl: "https://example.test/studio.png" }),
      ],
    });

    expect(structuralChildProposals(root).map((child) => child.proposalId)).toEqual(["season-1"]);
    expect(relationshipProposals(root).map((child) => child.proposalId)).toEqual(["actor-1", "studio-1"]);
    expect(reviewChildProposals(root).map((child) => child.proposalId)).toEqual(["season-1", "actor-1", "studio-1"]);
    expect(findRelationshipImage(root, "person", "Series Actor")).toBe("https://example.test/actor.jpg");
  });

  it("groups structural children and relationships into separate review rows", () => {
    const root = proposal("series", "video-series", {
      children: [
        proposal("season-1", "video-season"),
        proposal("season-2", "video-season"),
      ],
      relationships: [
        proposal("actor-1", "person", { title: "Series Actor" }),
        proposal("studio-1", "studio", { title: "Chair Pictures" }),
      ],
    });

    expect(groupProposalRows(structuralChildProposals(root))).toEqual([
      { id: "video-season", label: "Seasons", proposals: [root.children[0], root.children[1]] },
    ]);
    expect(groupProposalRows(relationshipProposals(root))).toEqual([
      { id: "person", label: "People", proposals: [root.relationships[0]] },
      { id: "studio", label: "Studios", proposals: [root.relationships[1]] },
    ]);
  });

  it("keeps nested cascade selections and relationship proposals in the apply payload", () => {
    const guest = proposal("guest", "person", {
      title: "Guest Actor",
      imageKind: "poster",
      imageUrl: "https://example.test/guest.jpg",
    });
    const episode = proposal("episode-2", "video", {
      title: "The Chair Company S01E02",
      credits: [{ name: "Guest Actor", role: "guest", character: "Visitor", sortOrder: 0 }],
      relationships: [guest],
    });
    const season = proposal("season-1", "video-season", {
      title: "Season 1",
      children: [episode],
    });
    const root = proposal("chair", "video-series", {
      title: "The Chair Company",
      children: [season],
    });

    const payload = buildProposalForApply(root, {
      selectedFieldsByProposal: {
        chair: { title: true, credits: false, images: true },
        "season-1": { title: true, credits: false, images: true },
        "episode-2": { title: true, credits: true, images: true },
      },
      selectedImagesByProposal: {},
      selectedCreditsByProposal: {
        "episode-2": { "guest:Guest Actor:Visitor:0": true },
      },
      selectedTagsByProposal: {},
      selectedCascade: {
        "season-1": true,
        "episode-2": true,
      },
    });

    const payloadSeason = expectSingle(payload.children);
    const payloadEpisode = expectSingle(payloadSeason.children);
    expect(payload.patch.title).toBe("The Chair Company");
    expect(payloadSeason.patch.title).toBe("Season 1");
    expect(payloadEpisode.patch.title).toBe("The Chair Company S01E02");
    expect(payloadEpisode.patch.credits).toEqual([{ name: "Guest Actor", role: "guest", character: "Visitor", sortOrder: 0 }]);
    expect(expectSingle(payloadEpisode.relationships).patch.title).toBe("Guest Actor");
  });

  it("uses related selections to exclude relationship-backed patch values", () => {
    const actor = proposal("actor-1", "person", { title: "Series Actor" });
    const studio = proposal("studio-1", "studio", { title: "HBO" });
    const root = proposal("series", "video-series", {
      studio: "HBO",
      credits: [{ name: "Series Actor", role: "actor", character: "Host", sortOrder: 0 }],
      relationships: [actor, studio],
    });

    const payload = buildProposalForApply(root, {
      selectedFieldsByProposal: {
        series: { credits: true, studio: true },
      },
      selectedImagesByProposal: {},
      selectedCreditsByProposal: {
        series: { "actor:Series Actor:Host:0": true },
      },
      selectedTagsByProposal: {},
      selectedCascade: {
        "actor-1": false,
        "studio-1": false,
      },
    });

    expect(payload.patch.credits).toEqual([]);
    expect(payload.patch.studio).toBeNull();
    expect(payload.relationships).toEqual([]);
  });

  it("resolves existing tag and credit titles from relationship ids", () => {
    const titles = relationshipTitlesFromEntityThumbnails(
      {
        relationships: [
          { kind: "tag", label: "Tags", entities: [thumbnail("tag-comedy", "tag", "COMEDY"), thumbnail("tag-drama", "tag", "Drama")] },
          { kind: "person", label: "Cast", entities: [thumbnail("person-tim", "person", "Tim Robinson")] },
        ],
      },
      [
        thumbnail("tag-comedy", "tag", "COMEDY"),
        thumbnail("tag-drama", "tag", "Drama"),
        thumbnail("person-tim", "person", "Tim Robinson"),
      ],
    );

    expect(titles.tags).toEqual(["COMEDY", "Drama"]);
    expect(titles.credits).toEqual(["Tim Robinson"]);
    expect(isNewRelationshipTitle("Comedy", titles.tags)).toBe(false);
    expect(isNewRelationshipTitle("Mystery", titles.tags)).toBe(true);
  });
});

function proposal(
  proposalId: string,
  targetKind: string,
  options: {
    title?: string;
    imageKind?: string;
    imageUrl?: string;
    studio?: string;
    credits?: EntityMetadataProposal["patch"]["credits"];
    children?: EntityMetadataProposal[];
    relationships?: EntityMetadataProposal[];
  } = {},
): EntityMetadataProposal {
  return {
    proposalId,
    provider: "tmdb",
    targetKind,
    confidence: 1,
    matchReason: "test",
    patch: {
      title: options.title ?? null,
      description: null,
      externalIds: {},
      urls: [],
      tags: [],
      studio: options.studio ?? null,
      credits: options.credits ?? [],
      dates: {},
      stats: {},
      positions: {},
      classification: null,
    },
    images: options.imageUrl ? [{ kind: options.imageKind ?? "poster", url: options.imageUrl, source: "tmdb" }] : [],
    children: options.children ?? [],
    relationships: options.relationships ?? [],
    candidates: [],
    targetEntityId: null,
  };
}

function expectSingle<T>(items: T[]): T {
  expect(items).toHaveLength(1);
  return items[0];
}

function thumbnail(id: string, kind: string, title: string) {
  return {
    id,
    kind,
    title,
    parentEntityId: null,
    sortOrder: null,
    coverUrl: null,
    hoverKind: "none",
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: false,
  };
}
