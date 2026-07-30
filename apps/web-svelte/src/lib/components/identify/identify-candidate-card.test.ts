import { describe, expect, it } from "vitest";

import { ENTITY_KIND } from "$lib/api/generated/codes";
import type { EntitySearchCandidate } from "$lib/api/generated/model";
import { aspectRatioForKind } from "$lib/entities/entity-thumbnail";

import {
	identifyCandidateKey,
	identifyCandidateToThumbnailCard,
} from "./identify-candidate-card";

describe("identify candidate cards", () => {
	it("maps search candidates into list thumbnail cards with poster and provider details", () => {
		const candidate: EntitySearchCandidate = {
			externalIds: {
				imdb: "tt1234567",
				tmdb: "271267",
			},
			overview: "A family man investigates a far-reaching conspiracy.",
			popularity: 42.84,
			posterUrl: "https://image.tmdb.org/t/p/w500/poster.jpg",
			title: "The Chair Company",
			year: 2025,
			candidateId: null,
			source: null,
			confidence: null,
			matchReason: null,
		};

		const card = identifyCandidateToThumbnailCard(candidate, ENTITY_KIND.videoSeries, 0);

		expect(card.entity.id).toBe("tmdb:271267");
		expect(card.entity.kind).toBe(ENTITY_KIND.videoSeries);
		expect(card.cover?.src).toBe(candidate.posterUrl);
		expect(card.cover?.alt).toBe(candidate.title);
		expect(card.aspectRatio).toEqual(aspectRatioForKind(ENTITY_KIND.videoSeries));
		expect(card.subtitle).toBe("2025");
		expect(card.meta?.map((item) => item.label)).toEqual([
			"tmdb: 271267",
			"imdb: tt1234567",
			"pop 42.8",
		]);
	});

	it("falls back to a stable candidate key when provider ids are missing", () => {
		const candidate: EntitySearchCandidate = {
			externalIds: {},
			overview: null,
			popularity: null,
			posterUrl: null,
			title: "Friendship",
			year: 2025,
			candidateId: null,
			source: null,
			confidence: null,
			matchReason: null,
		};

		expect(identifyCandidateKey(candidate, 3)).toBe("candidate:friendship:2025:3");
	});
});
