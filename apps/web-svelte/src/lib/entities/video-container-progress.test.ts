import { describe, expect, it } from "vitest";
import {
  CAPABILITY_KIND,
  ENTITY_KIND,
  PROGRESS_UNIT,
  THUMBNAIL_HOVER_KIND,
} from "$lib/api/generated/codes";
import type { EntityCapabilityProgressCapability } from "$lib/api/generated/model";
import {
  videoContainerProgressDisplay,
  videoProgressEpisodeFromCard,
  type VideoProgressEpisode,
} from "./video-container-progress";
import type { EntityThumbnailCard } from "./entity-thumbnail";

describe("videoContainerProgressDisplay", () => {
  it("keeps the current episode separate from watched coverage", () => {
    const display = videoContainerProgressDisplay(
      progress({ currentEntityId: "episode-3", index: 2, total: 4, consumedCount: 2, consumedPercent: 0.5 }),
      episode({ id: "episode-3", resumeSeconds: 50, durationSeconds: 100 }),
    );

    expect(display).toMatchObject({
      episodeId: "episode-3",
      percent: 50,
      positionLabel: "Current · Episode 3 of 4",
      episodeLabel: "Episode Three · 2 of 4 watched",
      completed: false,
    });
  });

  it("moves current backward without reducing watched coverage", () => {
    const display = videoContainerProgressDisplay(
      progress({ currentEntityId: "episode-2", index: 1, total: 4, consumedCount: 3, consumedPercent: 0.75 }),
      episode({ id: "episode-2", resumeSeconds: 0, durationSeconds: 100 }),
    );

    expect(display).toMatchObject({
      episodeId: "episode-2",
      percent: 75,
      positionLabel: "Current · Episode 2 of 4",
      canContinue: true,
    });
  });

  it("shows a completed container at one hundred percent", () => {
    const display = videoContainerProgressDisplay(
      progress({ currentEntityId: "episode-4", index: 3, total: 4, completedAt: "2026-07-15T12:00:00Z" }),
      episode({ id: "episode-4", resumeSeconds: 0, durationSeconds: 100 }),
    );

    expect(display).toMatchObject({ percent: 100, completed: true, canContinue: false });
  });

  it("uses a lightweight episode thumbnail meter when capabilities are not hydrated", () => {
    const episode = videoProgressEpisodeFromCard({
      progress: 0.5,
      aspectRatio: "video",
      cover: null,
      hover: { kind: THUMBNAIL_HOVER_KIND.none },
      entity: {
        id: "episode-3",
        kind: ENTITY_KIND.videoEpisode,
        title: "Episode Three",
        parentEntityId: null,
        sortOrder: 3,
        capabilities: [],
        childrenByKind: [],
        relationships: [],
      },
    } satisfies EntityThumbnailCard);

    const display = videoContainerProgressDisplay(
      progress({ currentEntityId: "episode-3", index: 2, total: 4, consumedCount: 2, consumedPercent: 0.5 }),
      episode,
    );

    expect(display?.percent).toBe(50);
  });
});

function progress(overrides: Partial<EntityCapabilityProgressCapability>): EntityCapabilityProgressCapability {
  return {
    kind: CAPABILITY_KIND.progress,
    currentEntityId: null,
    unit: PROGRESS_UNIT.item,
    index: 0,
    total: 0,
    mode: null,
    completedAt: null,
    updatedAt: null,
    ...overrides,
  };
}

function episode(overrides: Partial<VideoProgressEpisode>): VideoProgressEpisode {
  return {
    id: "episode-1",
    title: "Episode Three",
    resumeSeconds: 0,
    durationSeconds: null,
    completedAt: null,
    ...overrides,
  };
}
