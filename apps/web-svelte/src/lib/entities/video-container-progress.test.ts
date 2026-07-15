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
  it("includes the partial episode fraction in the container percentage", () => {
    const display = videoContainerProgressDisplay(
      progress({ currentEntityId: "episode-3", index: 2, total: 4 }),
      episode({ id: "episode-3", resumeSeconds: 50, durationSeconds: 100 }),
    );

    expect(display).toMatchObject({
      episodeId: "episode-3",
      percent: 62.5,
      positionLabel: "Episode 3 of 4",
      episodeLabel: "Episode Three",
      completed: false,
    });
  });

  it("offers the next unstarted episode as the current continue target", () => {
    const display = videoContainerProgressDisplay(
      progress({ currentEntityId: "episode-2", index: 1, total: 4 }),
      episode({ id: "episode-2", resumeSeconds: 0, durationSeconds: 100 }),
    );

    expect(display).toMatchObject({
      episodeId: "episode-2",
      percent: 25,
      positionLabel: "Episode 2 of 4",
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
        kind: ENTITY_KIND.video,
        title: "Episode Three",
        parentEntityId: null,
        sortOrder: 3,
        capabilities: [],
        childrenByKind: [],
        relationships: [],
      },
    } satisfies EntityThumbnailCard);

    const display = videoContainerProgressDisplay(
      progress({ currentEntityId: "episode-3", index: 2, total: 4 }),
      episode,
    );

    expect(display?.percent).toBe(62.5);
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
