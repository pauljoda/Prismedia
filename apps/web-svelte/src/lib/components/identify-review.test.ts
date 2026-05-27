import { describe, expect, it } from "vitest";
import type { EntityMetadataProposal } from "$lib/api/identify-types";
import type { EntityCard as EntityDetailCard } from "$lib/api/generated/model";
import {
  buildRootReviewApplyPayload,
  buildProposalForApply,
  currentFieldValueForReview,
  defaultImageSelectionForReview,
  defaultFieldSelectionForReview,
  findRelationshipImage,
  groupProposalRows,
  isNewRelationshipTitle,
  proposalFieldValue,
  reviewDiffFieldKeys,
  reviewFieldKeys,
  reviewImagePreviewUrl,
  reviewPositionValue,
  reviewableImages,
  reviewChildProposals,
  relationshipProposals,
  relationshipTitlesFromEntityThumbnails,
  scopedCreditForProposal,
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
    const tag = proposal("tag-1", "tag", { title: "Comedy" });
    const root = proposal("series", "video-series", {
      studio: "HBO",
      tags: ["Comedy", "Mystery"],
      credits: [{ name: "Series Actor", role: "actor", character: "Host", sortOrder: 0 }],
      relationships: [actor, studio, tag],
    });

    const payload = buildProposalForApply(root, {
      selectedFieldsByProposal: {
        series: { credits: true, studio: true, tags: true },
      },
      selectedImagesByProposal: {},
      selectedCreditsByProposal: {
        series: { "actor:Series Actor:Host:0": true },
      },
      selectedTagsByProposal: {},
      selectedCascade: {
        "actor-1": false,
        "studio-1": false,
        "tag-1": false,
      },
    });

    expect(payload.patch.credits).toEqual([]);
    expect(payload.patch.studio).toBeNull();
    expect(payload.patch.tags).toEqual(["Mystery"]);
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

  it("builds a root apply payload from reviewed artwork, tags, and scoped credits", () => {
    const actor = proposal("actor-1", "person", {
      title: "Tim Robinson",
      imageKind: "poster",
      imageUrl: "https://example.test/tim.jpg",
    });
    const studio = proposal("studio-1", "studio", { title: "HBO" });
    const root = proposal("series", "video-series", {
      title: "The Chair Company",
      studio: "HBO",
      credits: [{ name: "Tim Robinson", role: "cast", character: "Ron Trosper", sortOrder: 0 }],
      relationships: [actor, studio],
    });
    root.patch.tags = ["Comedy", "Drama", "Mystery"];
    root.images = [
      { kind: "poster", url: "https://example.test/poster.jpg", source: "tmdb" },
      { kind: "backdrop", url: "https://example.test/backdrop.jpg", source: "tmdb" },
      { kind: "logo", url: "https://example.test/logo.png", source: "tmdb" },
    ];

    expect(reviewableImages(root.images).map((image) => image.kind)).toEqual(["poster", "backdrop"]);
    expect(defaultImageSelectionForReview(root)).toEqual({
      poster: "https://example.test/poster.jpg",
      backdrop: "https://example.test/backdrop.jpg",
    });
    expect(scopedCreditForProposal(root, actor)).toMatchObject({
      role: "cast",
      character: "Ron Trosper",
    });

    const payload = buildRootReviewApplyPayload(root, {
      selectedFields: {
        title: true,
        tags: true,
        credits: true,
        images: true,
      },
      selectedImages: {
        poster: "https://example.test/poster.jpg",
        backdrop: null,
        logo: "https://example.test/logo.png",
      },
      selectedTags: {
        Comedy: true,
        Drama: false,
        Mystery: true,
      },
      selectedCascade: {
        "actor-1": false,
        "studio-1": false,
      },
    });

    expect(payload.selectedFields).toContain("images");
    expect(payload.selectedImages).toEqual({ poster: "https://example.test/poster.jpg" });
    expect(payload.proposal.patch.tags).toEqual(["Comedy", "Mystery"]);
    expect(payload.proposal.patch.credits).toEqual([]);
    expect(payload.proposal.patch.studio).toBeNull();
    expect(payload.proposal.relationships).toEqual([]);
    expect(payload.proposal.images.map((image) => image.kind)).toEqual(["poster"]);
  });

  it("uses smaller TMDB image variants for review previews without changing selected image URLs", () => {
    expect(reviewImagePreviewUrl({
      kind: "poster",
      url: "https://image.tmdb.org/t/p/original/poster.jpg",
      source: "tmdb",
    })).toBe("https://image.tmdb.org/t/p/w342/poster.jpg");
    expect(reviewImagePreviewUrl({
      kind: "poster",
      url: "https://image.tmdb.org/t/p/original/person.jpg",
      source: "tmdb",
    }, "person")).toBe("https://image.tmdb.org/t/p/w185/person.jpg");
    expect(reviewImagePreviewUrl({
      kind: "backdrop",
      url: "https://image.tmdb.org/t/p/original/backdrop.jpg",
      source: "tmdb",
    })).toBe("https://image.tmdb.org/t/p/w780/backdrop.jpg");
    expect(reviewImagePreviewUrl({
      kind: "logo",
      url: "https://image.tmdb.org/t/p/original/logo.png",
      source: "tmdb",
    })).toBe("https://image.tmdb.org/t/p/w300/logo.png");
    expect(reviewImagePreviewUrl({
      kind: "poster",
      url: "https://example.test/poster.jpg",
      source: "other",
    })).toBe("https://example.test/poster.jpg");
  });

  it("carries walked child field and artwork choices into the root apply payload", () => {
    const episode = proposal("episode-1", "video", {
      title: "Episode 1",
      imageKind: "poster",
      imageUrl: "https://example.test/episode-poster.jpg",
    });
    episode.patch.description = "Episode description";
    episode.images.push({ kind: "backdrop", url: "https://example.test/episode-backdrop.jpg", source: "tmdb" });
    const root = proposal("series", "video-series", {
      title: "Series",
      children: [episode],
    });

    const payload = buildRootReviewApplyPayload(root, {
      selectedFields: {
        title: true,
        images: true,
      },
      selectedImages: {},
      selectedFieldsByProposal: {
        "episode-1": {
          title: true,
          description: false,
          images: true,
        },
      },
      selectedImagesByProposal: {
        "episode-1": {
          poster: "https://example.test/episode-poster.jpg",
          backdrop: null,
        },
      },
    });

    const payloadEpisode = expectSingle(payload.proposal.children);
    expect(payloadEpisode.patch.title).toBe("Episode 1");
    expect(payloadEpisode.patch.description).toBeNull();
    expect(payloadEpisode.images.map((image) => image.url)).toEqual(["https://example.test/episode-poster.jpg"]);
  });

  it("keeps rich selector fields out of the field diff but selected for apply defaults", () => {
    const root = proposal("series", "video-series", {
      title: "Series",
      studio: "HBO",
      tags: ["Comedy"],
      credits: [{ name: "Tim Robinson", role: "cast", character: "Ron", sortOrder: 0 }],
      imageKind: "poster",
      imageUrl: "https://example.test/poster.jpg",
    });

    expect(reviewDiffFieldKeys).not.toEqual(expect.arrayContaining(["tags", "studio", "credits", "images"]));
    expect(reviewDiffFieldKeys).toEqual(["title", "description", "externalIds", "urls", "dates", "stats", "positions", "classification"]);

    const selection = defaultFieldSelectionForReview(root);
    expect(Object.keys(selection)).toEqual([...reviewFieldKeys]);
    expect(selection.title).toBe(true);
    expect(selection.tags).toBe(true);
    expect(selection.studio).toBe(true);
    expect(selection.credits).toBe(true);
    expect(selection.images).toBe(true);
  });

  it("describes season and episode positions as sort-order changes", () => {
    expect(reviewPositionValue({ seasonNumber: 1 }, "video-season")).toBe("Sort order: Season 1");
    expect(reviewPositionValue({ episodeNumber: 2 }, "video")).toBe("Sort order: Episode 2");
    expect(reviewPositionValue({ sortOrder: 3 }, "video-season")).toBe("Sort order: Season 3");
    expect(reviewPositionValue({ seasonNumber: 1, episodeNumber: 2 }, "video")).toBe("Season: Season 1, Sort order: Episode 2");

    const season = proposal("season-1", "video-season");
    season.patch.positions = { seasonNumber: 1 };
    expect(proposalFieldValue(season, "positions")).toBe("Sort order: Season 1");
  });

  it("allows studio logo artwork to be reviewed and carried through walked relationship selections", () => {
    const studio = proposal("studio-1", "studio", {
      title: "HBO",
      imageKind: "logo",
      imageUrl: "https://example.test/logo.png",
    });
    const root = proposal("series", "video-series", {
      studio: "HBO",
      relationships: [studio],
    });

    expect(reviewableImages(studio.images, studio.targetKind).map((image) => image.kind)).toEqual(["logo"]);
    expect(defaultImageSelectionForReview(studio)).toEqual({ logo: "https://example.test/logo.png" });

    const payload = buildProposalForApply(root, {
      selectedFieldsByProposal: {
        series: defaultFieldSelectionForReview(root),
        "studio-1": defaultFieldSelectionForReview(studio),
      },
      selectedImagesByProposal: {
        "studio-1": {
          logo: "https://example.test/logo.png",
        },
      },
      selectedCreditsByProposal: {},
      selectedTagsByProposal: {},
      selectedCascade: {},
    });

    expect(expectSingle(payload.relationships).images.map((image) => image.kind)).toEqual(["logo"]);
  });

  it("reads current values from a walked child entity detail", () => {
    const detail: EntityDetailCard = {
      id: "episode-1",
      kind: "video",
      title: "Existing episode",
      parentEntityId: "season-1",
      sortOrder: 1,
      capabilities: [
        { kind: "description", value: "Current description" },
        { kind: "images", supportedKinds: ["poster", "backdrop"], items: [
          { kind: "poster", path: "/assets/poster.jpg", mimeType: "image/jpeg" },
          { kind: "backdrop", path: "/assets/backdrop.jpg", mimeType: "image/jpeg" },
        ], thumbnailUrl: null, coverUrl: null },
      ],
      childrenByKind: [],
      relationships: [
        { kind: "studio", label: "Studios", entities: [thumbnail("studio-1", "studio", "Existing Studio")] },
      ],
    };

    expect(currentFieldValueForReview(thumbnail("episode-1", "video", "Fallback title"), detail, "title")).toBe("Existing episode");
    expect(currentFieldValueForReview(thumbnail("episode-1", "video", "Fallback title"), detail, "description")).toBe("Current description");
    expect(currentFieldValueForReview(thumbnail("episode-1", "video", "Fallback title"), detail, "studio")).toBe("Existing Studio");
    expect(currentFieldValueForReview(thumbnail("episode-1", "video", "Fallback title"), detail, "images")).toBe("poster, backdrop");
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
    tags?: string[];
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
      tags: options.tags ?? [],
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
