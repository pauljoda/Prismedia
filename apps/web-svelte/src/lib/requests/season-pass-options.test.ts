import { describe, expect, it } from "vitest";
import {
  CAPABILITY_KIND,
  ENTITY_KIND,
  EXTERNAL_ID_PROVIDER,
  PROPOSAL_KIND,
  REQUEST_MEDIA_KIND,
} from "$lib/api/generated/codes";
import type { EntityMetadataProposal, ExternalIdentity, RequestReviewResponse } from "$lib/api/generated/model";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { buildSeasonPassRows } from "./season-pass-options";

function seasonCard(
  id: string,
  title: string,
  number: number,
  identity: ExternalIdentity,
): EntityThumbnailCard {
  return {
    entity: {
      id,
      kind: ENTITY_KIND.videoSeason,
      title,
      parentEntityId: "series-1",
      sortOrder: number,
      capabilities: [{
        kind: CAPABILITY_KIND.links,
        externalIds: [{ provider: identity.namespace, value: identity.value, url: null }],
        urls: [],
      } as never],
      childrenByKind: [],
      relationships: [],
    },
    aspectRatio: "video",
    cover: null,
    hover: { kind: "none" },
  };
}

describe("season pass options", () => {
  it("combines local Entities with direct reviewed season proposals without flattening opaque identities", () => {
    const seasonFiveIdentity = { namespace: "tmdbseason", value: "Show:AbC:01:5" };
    const review = seriesReview([
      seasonProposal("specials", "Specials"),
      seasonProposal("season-5", "Season 5"),
      seasonProposal("season-6", "Season 6"),
    ], [
      seasonTarget("specials", { namespace: "tmdbseason", value: "Show:AbC:01:0" }, 0),
      seasonTarget("season-5", seasonFiveIdentity, 5),
      seasonTarget("season-6", { namespace: "tmdbseason", value: "Show:AbC:01:6" }, 6),
    ]);

    const rows = buildSeasonPassRows({
      localSeasons: [seasonCard("local-s5", "Season 5", 5, seasonFiveIdentity)],
      episodeCounts: { "local-s5": 20 },
      providerReview: review,
    });

    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({
      entityId: "local-s5",
      proposalId: "season-5",
      externalIdentity: seasonFiveIdentity,
      episodes: 20,
    });
    expect(rows[1]).toMatchObject({
      entityId: null,
      proposalId: "season-6",
      externalIdentity: { namespace: "tmdbseason", value: "Show:AbC:01:6" },
      number: 6,
    });
  });

  it("keeps local-only seasons actionable when provider review is unavailable", () => {
    const identity = { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Case:Sensitive:Value" };
    const rows = buildSeasonPassRows({
      localSeasons: [seasonCard("local-s2", "Season 2", 2, identity)],
      episodeCounts: { "local-s2": 8 },
      providerReview: null,
    });

    expect(rows).toEqual([expect.objectContaining({
      key: "local-s2",
      entityId: "local-s2",
      proposalId: null,
      externalIdentity: identity,
      episodes: 8,
    })]);
  });
});

function seriesReview(
  children: EntityMetadataProposal[],
  childTargets: RequestReviewResponse["targets"],
): RequestReviewResponse {
  const rootIdentity = { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01" };
  const root = proposal("series-root", PROPOSAL_KIND.videoSeries, "Series", children);
  return {
    pluginId: "cinema-metadata",
    externalIdentity: rootIdentity,
    entityKind: ENTITY_KIND.videoSeries,
    kind: REQUEST_MEDIA_KIND.series,
    proposal: root,
    revision: "review-revision",
    targets: [{
      proposalId: root.proposalId,
      kind: REQUEST_MEDIA_KIND.series,
      entityKind: ENTITY_KIND.videoSeries,
      externalIdentity: rootIdentity,
      requestable: true,
    }, ...childTargets],
  };
}

function seasonTarget(
  proposalId: string,
  externalIdentity: ExternalIdentity,
  position: number,
): RequestReviewResponse["targets"][number] {
  return {
    proposalId,
    kind: REQUEST_MEDIA_KIND.season,
    entityKind: ENTITY_KIND.videoSeason,
    externalIdentity,
    requestable: true,
    position,
  };
}

function seasonProposal(proposalId: string, title: string): EntityMetadataProposal {
  return proposal(proposalId, PROPOSAL_KIND.videoSeason, title);
}

function proposal(
  proposalId: string,
  targetKind: EntityMetadataProposal["targetKind"],
  title: string,
  children: EntityMetadataProposal[] = [],
): EntityMetadataProposal {
  return {
    proposalId,
    provider: "cinema-metadata",
    targetKind,
    confidence: 1,
    matchReason: "external-id",
    patch: {
      title,
      description: null,
      externalIds: {},
      urls: [],
      tags: [],
      studio: null,
      credits: [],
      dates: {},
      stats: {},
      positions: {},
      classification: null,
    },
    images: [],
    children,
    relationships: [],
    candidates: [],
  };
}
