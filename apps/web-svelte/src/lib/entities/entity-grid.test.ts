import { describe, expect, it } from "vitest";
import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
import type { EntityCapability, EntityCard, EntityThumbnail, EntityKind } from "$lib/api/generated/model";
import {
  AVAILABILITY_FILTER_DEFS,
  ENTITY_GRID_ALL_KINDS,
  applyEntityGridState,
  buildCapabilityFilterOptions,
  buildEntityKindTabs,
  buildServerQueryFromFilters,
  entityCardToThumbnailCard,
  entityGridRequestFromState,
  isServerResolvedFilterId,
  normalizeEntityGridFilterIds,
  type EntityGridState,
} from "./entity-grid";

function flags(isNsfw = false): EntityCapability {
  return {
    kind: "flags",
    isFavorite: true,
    isNsfw,
    isOrganized: true,
  };
}

function rating(value: number): EntityCapability {
  return {
    kind: "rating",
    value,
  };
}

function image(): EntityCapability {
  return {
    kind: "images",
    supportedKinds: ["cover", "preview"],
    thumbnailUrl: "/cover.jpg",
    thumbnail2xUrl: null,
    coverUrl: "/cover.jpg",
    items: [
      { kind: "cover", path: "/cover.jpg", mimeType: "image/jpeg" },
      { kind: "preview", path: "/preview.jpg", mimeType: "image/jpeg" },
    ],
  };
}

function stats(): EntityCapability {
  return {
    kind: "stats",
    items: [{ code: "images", value: 12 }],
  };
}

function technical(): EntityCapability {
  return {
    kind: "technical",
    duration: "00:02:30",
    width: 1920,
    height: 1080,
    frameRate: null,
    bitRate: null,
    sampleRate: null,
    channels: null,
    codec: "h264",
    container: null,
    format: null,
  };
}

function position(items: { code: string; value: number; label?: string | null }[]): EntityCapability {
  return {
    kind: "position",
    items: items.map((item) => ({ ...item, label: item.label ?? null })),
  };
}

function card(id: string, kind: EntityKind, title: string, capabilities: EntityCapability[]): EntityCard {
  return { id, kind, title, parentEntityId: null, sortOrder: null, capabilities, childrenByKind: [], relationships: [] };
}

describe("entity grid helpers", () => {
  const cards = [
    entityCardToThumbnailCard(card("1", "video", "Safe Video", [flags(false), rating(5), image(), technical()])),
    entityCardToThumbnailCard(card("2", "gallery", "Hidden Gallery", [flags(true), rating(2), image(), stats()])),
    entityCardToThumbnailCard(card("3", "book", "Safe Book", [flags(false), image(), stats()])),
  ];

  it("builds tabs from mixed entity kinds", () => {
    expect(buildEntityKindTabs(cards)).toEqual([
      { kind: "book", label: "Books", count: 1 },
      { kind: "gallery", label: "Galleries", count: 1 },
      { kind: "video", label: "Videos", count: 1 },
    ]);
  });

  it("uses logo artwork as a card fallback when no poster or cover exists", () => {
    const thumbnail = entityCardToThumbnailCard(card("studio-1", "studio", "HBO", [
      {
        kind: "images",
        supportedKinds: ["logo"],
        thumbnailUrl: null,
        thumbnail2xUrl: null,
        coverUrl: null,
        items: [{ kind: "logo", path: "/assets/studios/hbo-logo.png", mimeType: "image/png" }],
      },
    ]));

    expect(thumbnail.cover?.src).toBe("/assets/studios/hbo-logo.png");
  });

  it("builds tabs from SFW-visible cards only", () => {
    expect(buildEntityKindTabs(cards, { includeNsfw: false })).toEqual([
      { kind: "book", label: "Books", count: 1 },
      { kind: "video", label: "Videos", count: 1 },
    ]);
  });

  it("derives capability filters from returned entities", () => {
    const options = buildCapabilityFilterOptions(cards);

    expect(options.map((option) => option.id)).toContain("rating:4");
    expect(options.map((option) => option.id)).toContain("images:preview");
    expect(options.map((option) => option.id)).toContain("stats:images");
    expect(options.map((option) => option.id)).toContain("technical:codec:h264");
  });

  it("filters NSFW cards before applying tabs and capability filters", () => {
    const options = buildCapabilityFilterOptions(cards);
    const visible = applyEntityGridState(cards, {
      activeKind: ENTITY_GRID_ALL_KINDS,
      filterIds: ["rating:4"],
      includeNsfw: false,
      query: "safe",
      sortBy: "title",
      sortDir: "asc",
      randomSeed: 0,
    }, options);

    expect(visible.map((item) => item.entity.id)).toEqual(["1"]);
  });

  it("sorts videos by episode position when position sorting is selected", () => {
    const visible = applyEntityGridState([
      entityCardToThumbnailCard(card("episode-10", "video", "Episode Ten", [position([{ code: "episode", value: 10 }])])),
      entityCardToThumbnailCard(card("episode-2", "video", "Episode Two", [position([{ code: "episode", value: 2 }])])),
      entityCardToThumbnailCard(card("special", "video", "Behind the Scenes", [])),
    ], {
      activeKind: ENTITY_GRID_ALL_KINDS,
      filterIds: [],
      includeNsfw: true,
      query: "",
      sortBy: "position",
      sortDir: "asc",
      randomSeed: 0,
    });

    expect(visible.map((item) => item.entity.id)).toEqual(["episode-2", "episode-10", "special"]);
  });

  it("preserves incoming order for duplicate position values", () => {
    const visible = applyEntityGridState([
      entityCardToThumbnailCard(card("first-added", "video", "Z Title", [position([{ code: "episode", value: 2 }])])),
      entityCardToThumbnailCard(card("second-added", "video", "A Title", [position([{ code: "episode", value: 2 }])])),
    ], {
      activeKind: ENTITY_GRID_ALL_KINDS,
      filterIds: [],
      includeNsfw: true,
      query: "",
      sortBy: "position",
      sortDir: "asc",
      randomSeed: 0,
    });

    expect(visible.map((item) => item.entity.id)).toEqual(["first-added", "second-added"]);
  });

  it("serializes request state for backend filtering", () => {
    const options = buildCapabilityFilterOptions(cards);
    const request = entityGridRequestFromState({
      activeKind: "video",
      filterIds: ["rating:4", "technical:duration"],
      includeNsfw: false,
      query: "bunny",
      sortBy: "rating",
      sortDir: "desc",
      randomSeed: 0,
    }, options);

    expect(request).toMatchObject({
      kind: "video",
      query: "bunny",
      sortBy: "rating",
      sortDir: "desc",
    });
    expect("includeNsfw" in request).toBe(false);
    expect(request.filters.map((filter) => filter.id)).toEqual(["rating:4", "technical:duration"]);
  });

  it("maps video season and episode numbers into the bottom-left custom slot", () => {
    const thumbnail = entityCardToThumbnailCard(card("4", "video", "Episode", [
      flags(false),
      technical(),
      position([
        { code: "season", value: 1 },
        { code: "episode", value: 2 },
      ]),
    ]));

    expect(thumbnail.custom?.bottomLeft).toEqual({
      label: "S1 E2",
      title: "Season 1, Episode 2",
    });
    expect(thumbnail.meta?.map((item) => item.label)).not.toContain("season 1");
  });

  it("derives a progress fraction from playback and reading-progress capabilities", () => {
    const completed = entityCardToThumbnailCard(card("c-done", "video", "Watched", [
      { kind: "playback", playCount: 1, skipCount: 0, playDurationSeconds: 0, resumeSeconds: 0, lastPlayedAt: null, completedAt: "2026-05-01T00:00:00Z" },
    ]));
    const resuming = entityCardToThumbnailCard(card("c-mid", "video", "Mid", [
      { kind: "playback", playCount: 0, skipCount: 0, playDurationSeconds: 0, resumeSeconds: 75, lastPlayedAt: null, completedAt: null },
      technical(), // 00:02:30 == 150s
    ]));
    const reading = entityCardToThumbnailCard(card("c-book", "book", "Reading", [
      { kind: "progress", currentEntityId: null, unit: "page", index: 5, total: 10, mode: null, completedAt: null, updatedAt: null },
    ]));
    const noProgress = entityCardToThumbnailCard(card("c-none", "video", "Fresh", [flags(false)]));

    expect(completed.progress).toBe(1);
    expect(resuming.progress).toBeCloseTo(0.5);
    expect(reading.progress).toBeCloseTo(0.5);
    expect(noProgress.progress).toBeNull();
  });

  it("uses a lightweight thumbnail row's precomputed progress field", () => {
    const thumbnail = entityCardToThumbnailCard({
      ...thumbnailEntity("row-1", "video", "Row"),
      progress: 0.42,
    });

    expect(thumbnail.progress).toBeCloseTo(0.42);
  });

  it("carries source-media availability from lightweight thumbnails", () => {
    const thumbnail = entityCardToThumbnailCard({
      ...thumbnailEntity("row-source", "video", "Stored"),
      hasSourceMedia: true,
    });

    expect(thumbnail.hasSourceMedia).toBe(true);
  });

  it("uses cover fit for entity thumbnail images by default", () => {
    const thumbnail = entityCardToThumbnailCard(card("6", "person", "Performer", [
      flags(false),
      image(),
    ]));

    expect(thumbnail.fit).toBe("cover");
  });

  it("maps Jellyfin image-playlist trickplay assets into sprite hover data", () => {
    const thumbnail = entityCardToThumbnailCard(card("5", "video", "Tiled Trickplay", [
      {
        kind: "images",
        supportedKinds: ["thumbnail", "trickplay"],
        thumbnailUrl: "/assets/videos/5/thumb.jpg",
        thumbnail2xUrl: null,
        coverUrl: "/assets/videos/5/thumb.jpg",
        items: [
          { kind: "thumbnail", path: "/assets/videos/5/thumb.jpg", mimeType: "image/jpeg" },
          { kind: "trickplay", path: "/Videos/5/Trickplay/320/tiles.m3u8", mimeType: "application/vnd.apple.mpegurl" },
        ],
      },
    ]));

    expect(thumbnail.hover).toEqual({
      kind: "sprite",
      vttUrl: "/Videos/5/Trickplay/320/tiles.m3u8",
    });
  });

  it("maps API hover image arrays into image-sequence thumbnail previews", () => {
    const thumbnail = entityCardToThumbnailCard(thumbnailEntity("book-1", "book", "Manga", [
      { entityId: "page-1", title: "Page 1", path: "/assets/pages/1.jpg" },
      { entityId: "page-2", title: "Page 2", path: "/assets/pages/2.jpg" },
    ]));

    expect(thumbnail.cover).toBeNull();
    expect(thumbnail.hover).toEqual({
      kind: "image-sequence",
      assets: [
        { src: "/assets/pages/1.jpg", alt: "Page 1 preview", role: "preview", entityId: "page-1" },
        { src: "/assets/pages/2.jpg", alt: "Page 2 preview", role: "preview", entityId: "page-2" },
      ],
    });
  });

  it("filters bitrate labels from lightweight API thumbnail metadata", () => {
    const thumbnail = entityCardToThumbnailCard({
      ...thumbnailEntity("video-1", "video", "Episode"),
      meta: [
        { icon: "duration", label: "10:35" },
        { icon: "video", label: "4K" },
        { icon: "video", label: "8.5 Mbps" },
        { icon: "video", label: "MOV" },
      ],
    });

    expect(thumbnail.meta?.map((item) => item.label)).toEqual(["10:35", "4K", "MOV"]);
  });
});

function thumbnailEntity(
  id: string,
  kind: EntityKind,
  title: string,
  hoverImages: EntityThumbnail["hoverImages"] = [],
): EntityThumbnail {
  return {
    id,
    kind,
    title,
    parentEntityId: null,
    sortOrder: null,
    coverUrl: null,
    coverThumbUrl: null,
    hoverKind: "none",
    hoverUrl: null,
    hoverImages,
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: false,
  };
}

function gridState(overrides: Partial<EntityGridState> = {}): EntityGridState {
  return {
    activeKind: ENTITY_GRID_ALL_KINDS,
    filterIds: [],
    includeNsfw: true,
    query: "",
    sortBy: "title",
    sortDir: "asc",
    randomSeed: 0,
    ...overrides,
  };
}

function flaggedThumb(overrides: Partial<EntityThumbnail> & { id: string; title: string }): EntityThumbnail {
  return { ...thumbnailEntity(overrides.id, overrides.kind ?? "video", overrides.title), ...overrides };
}

describe("server-resolved filters and sorting", () => {
  it("identifies which filter ids the server resolves across the whole library", () => {
    expect(isServerResolvedFilterId("flags:favorite")).toBe(true);
    expect(isServerResolvedFilterId("flags:organized:true")).toBe(true);
    expect(isServerResolvedFilterId("rating:min:3")).toBe(true);
    expect(isServerResolvedFilterId("rating:max:4")).toBe(true);
    expect(isServerResolvedFilterId("rating:unrated")).toBe(true);
    expect(isServerResolvedFilterId("status:watched")).toBe(true);
    expect(isServerResolvedFilterId("availability:on-disk")).toBe(true);
    // Client-only filters stay client-resolved.
    expect(isServerResolvedFilterId("technical:codec:h264")).toBe(false);
    expect(isServerResolvedFilterId("dates:from:2026-01-01")).toBe(false);
  });

  it("folds active filter ids into a server query", () => {
    expect(
      buildServerQueryFromFilters([
        "flags:favorite",
        "flags:organized:true",
        "rating:min:3",
        "rating:max:5",
        "status:watched",
      ]),
    ).toEqual({ favorite: true, organized: true, ratingMin: 3, ratingMax: 5, status: "watched" });
  });

  it("collapses multiple rating bounds to the tightest window", () => {
    const server = buildServerQueryFromFilters(["rating:min:2", "rating:min:4", "rating:max:5", "rating:max:3"]);
    expect(server.ratingMin).toBe(4);
    expect(server.ratingMax).toBe(3);
  });

  it("maps the unrated and not-organized filters to server flags", () => {
    expect(buildServerQueryFromFilters(["rating:unrated"])).toEqual({ unrated: true });
    expect(buildServerQueryFromFilters(["flags:organized:false"])).toEqual({ organized: false });
  });

  it("serializes availability filters to canonical server parameters", () => {
    expect(buildServerQueryFromFilters(["availability:on-disk"])).toEqual({ hasFile: true });
    expect(buildServerQueryFromFilters(["availability:wanted"])).toEqual({ wanted: true });
    expect(buildServerQueryFromFilters(["availability:downloaded"])).toEqual({ acquisitionStatus: "downloaded" });
    expect(buildServerQueryFromFilters(["availability:failed"])).toEqual({ acquisitionStatus: "failed" });
    expect(buildServerQueryFromFilters([`availability:${ACQUISITION_STATUS.stopping}`])).toEqual({
      acquisitionStatus: ACQUISITION_STATUS.stopping,
    });
  });

  it("migrates legacy file filters to availability filters", () => {
    expect(normalizeEntityGridFilterIds(["files:has:true", "files:has:false", "flags:favorite"]))
      .toEqual(["availability:on-disk", "availability:wanted", "flags:favorite"]);
  });

  it("filters local grids by source media, wanted state, and latest acquisition status", () => {
    const localCards = [
      entityCardToThumbnailCard(flaggedThumb({ id: "stored", title: "Stored", hasSourceMedia: true })),
      entityCardToThumbnailCard(flaggedThumb({ id: "failed", title: "Failed", isWanted: true, wantedStatus: "failed", latestAcquisitionStatus: "failed" })),
      entityCardToThumbnailCard(flaggedThumb({ id: "searching", title: "Searching", isWanted: true, wantedStatus: "searching", latestAcquisitionStatus: "searching" })),
      entityCardToThumbnailCard(flaggedThumb({ id: "imported", title: "Imported", isWanted: false, latestAcquisitionStatus: "imported" })),
      entityCardToThumbnailCard(flaggedThumb({
        id: "stopping",
        title: "Cleaning up",
        isWanted: true,
        wantedStatus: ACQUISITION_STATUS.stopping,
        latestAcquisitionStatus: ACQUISITION_STATUS.stopping,
      })),
    ];

    const options = buildCapabilityFilterOptions(localCards, "video");
    expect(AVAILABILITY_FILTER_DEFS.map((definition) => definition.id)).toContain("availability:failed");
    expect(AVAILABILITY_FILTER_DEFS.map((definition) => definition.id)).toContain(
      `availability:${ACQUISITION_STATUS.stopping}`,
    );
    expect(applyEntityGridState(
      localCards,
      gridState({ filterIds: ["availability:on-disk"] }),
      options,
      { serverResolvedFilters: false },
    ).map((card) => card.entity.id)).toEqual(["stored"]);
    expect(applyEntityGridState(
      localCards,
      gridState({ filterIds: ["availability:failed"] }),
      options,
      { serverResolvedFilters: false },
    ).map((card) => card.entity.id)).toEqual(["failed"]);
    expect(applyEntityGridState(
      localCards,
      gridState({ filterIds: ["availability:imported"] }),
      options,
      { serverResolvedFilters: false },
    ).map((card) => card.entity.id)).toEqual(["imported"]);
    expect(applyEntityGridState(
      localCards,
      gridState({ filterIds: [`availability:${ACQUISITION_STATUS.stopping}`] }),
      options,
      { serverResolvedFilters: false },
    ).map((card) => card.entity.id)).toEqual(["stopping"]);
  });

  it("counts and matches every subtree acquisition status with singular fallback", () => {
    const subtree = entityCardToThumbnailCard(flaggedThumb({
      id: "series",
      title: "Series",
      latestAcquisitionStatus: ACQUISITION_STATUS.imported,
      acquisitionStatuses: [
        ACQUISITION_STATUS.imported,
        ACQUISITION_STATUS.downloading,
        ACQUISITION_STATUS.failed,
      ],
    } as Partial<EntityThumbnail> & { acquisitionStatuses: string[]; id: string; title: string }));
    const legacy = entityCardToThumbnailCard(flaggedThumb({
      id: "legacy",
      title: "Legacy",
      latestAcquisitionStatus: ACQUISITION_STATUS.failed,
    }));
    const cardsWithStatuses = [subtree, legacy];
    const options = buildCapabilityFilterOptions(cardsWithStatuses, "video-series");

    expect(options.find((option) => option.id === "availability:failed")?.count).toBe(2);
    expect(options.find((option) => option.id === "availability:downloading")?.count).toBe(1);
    expect(applyEntityGridState(
      cardsWithStatuses,
      gridState({ filterIds: ["availability:downloading"] }),
      options,
      { serverResolvedFilters: false },
    ).map((card) => card.entity.id)).toEqual(["series"]);
    expect(applyEntityGridState(
      cardsWithStatuses,
      gridState({ filterIds: ["availability:failed"] }),
      options,
      { serverResolvedFilters: false },
    ).map((card) => card.entity.id)).toEqual(["legacy", "series"]);
  });

  it("maps the taxonomy reference filters to the orphaned server flag in both directions", () => {
    expect(buildServerQueryFromFilters(["taxonomy:orphaned"])).toEqual({ orphaned: true });
    expect(buildServerQueryFromFilters(["taxonomy:referenced"])).toEqual({ orphaned: false });
  });

  it("folds book type and format filters into comma-separated server params", () => {
    const server = buildServerQueryFromFilters([
      "book-type:comic",
      "book-type:manga",
      "book-format:pdf",
    ]);
    expect(server.bookType).toBe("comic,manga");
    expect(server.bookFormat).toBe("pdf");
  });

  it("treats book type and format filters as server-resolved", () => {
    expect(isServerResolvedFilterId("book-type:comic")).toBe(true);
    expect(isServerResolvedFilterId("book-format:epub")).toBe(true);
  });

  it("does not re-filter the loaded page on book type/format filters", () => {
    const cards = [
      entityCardToThumbnailCard(thumbnailEntity("a", "book", "Comic A")),
      entityCardToThumbnailCard(thumbnailEntity("b", "book", "Novel B")),
    ];
    // Thumbnails carry no book type, so the server result must pass through untouched.
    const visible = applyEntityGridState(cards, gridState({ filterIds: ["book-type:comic"] }));
    expect(visible.map((card) => card.entity.id).sort()).toEqual(["a", "b"]);
  });

  it("emits a stable seed for the random sort", () => {
    const request = entityGridRequestFromState(gridState({ sortBy: "random", randomSeed: 4242 }), []);
    expect(request.server.sort).toBe("random");
    expect(request.server.seed).toBe(4242);
  });

  it("forwards date-added and rating sorts with direction, and leaves kind/position to the client", () => {
    expect(entityGridRequestFromState(gridState({ sortBy: "added", sortDir: "desc" }), []).server)
      .toMatchObject({ sort: "added", sortDir: "desc" });
    expect(entityGridRequestFromState(gridState({ sortBy: "rating", sortDir: "asc" }), []).server)
      .toMatchObject({ sort: "rating", sortDir: "asc" });
    expect(entityGridRequestFromState(gridState({ sortBy: "kind" }), []).server.sort).toBeUndefined();
    expect(entityGridRequestFromState(gridState({ sortBy: "position" }), []).server.sort).toBeUndefined();
  });

  it("forwards the reference-count sort with direction for taxonomy grids", () => {
    expect(entityGridRequestFromState(gridState({ sortBy: "references", sortDir: "desc" }), []).server)
      .toMatchObject({ sort: "references", sortDir: "desc" });
  });

  it("does not re-filter the loaded page on server-resolved filters", () => {
    const cards = [
      entityCardToThumbnailCard(flaggedThumb({ id: "fav", title: "Favorite", isFavorite: true })),
      entityCardToThumbnailCard(flaggedThumb({ id: "plain", title: "Plain" })),
    ];
    // The server already narrowed the result set, so the client keeps every card.
    const visible = applyEntityGridState(cards, gridState({ filterIds: ["flags:favorite"] }));
    expect(visible.map((card) => card.entity.id).sort()).toEqual(["fav", "plain"]);
  });

  it("preserves server order for random and date-added sorts", () => {
    const cards = [
      entityCardToThumbnailCard(thumbnailEntity("a", "video", "Zeta")),
      entityCardToThumbnailCard(thumbnailEntity("b", "video", "Alpha")),
    ];
    // Title sort would reorder to Alpha, Zeta; random/added keep arrival order.
    expect(applyEntityGridState(cards, gridState({ sortBy: "random", randomSeed: 1 })).map((c) => c.entity.id))
      .toEqual(["a", "b"]);
    expect(applyEntityGridState(cards, gridState({ sortBy: "added" })).map((c) => c.entity.id))
      .toEqual(["a", "b"]);
    expect(applyEntityGridState(cards, gridState({ sortBy: "references" })).map((c) => c.entity.id))
      .toEqual(["a", "b"]);
  });

  it("applies seeded random ordering for local-only grids", () => {
    const cards = [
      entityCardToThumbnailCard(thumbnailEntity("a", "gallery", "Sub Gallery A")),
      entityCardToThumbnailCard(thumbnailEntity("b", "gallery", "Sub Gallery B")),
      entityCardToThumbnailCard(thumbnailEntity("c", "gallery", "Sub Gallery C")),
      entityCardToThumbnailCard(thumbnailEntity("d", "gallery", "Sub Gallery D")),
    ];

    const firstShuffle = applyEntityGridState(
      cards,
      gridState({ sortBy: "random", randomSeed: 1 }),
      undefined,
      { preserveServerResolvedSorts: false },
    ).map((c) => c.entity.id);
    const sameShuffle = applyEntityGridState(
      cards,
      gridState({ sortBy: "random", randomSeed: 1 }),
      undefined,
      { preserveServerResolvedSorts: false },
    ).map((c) => c.entity.id);
    const reshuffle = applyEntityGridState(
      cards,
      gridState({ sortBy: "random", randomSeed: 2 }),
      undefined,
      { preserveServerResolvedSorts: false },
    ).map((c) => c.entity.id);

    expect(firstShuffle).toEqual(sameShuffle);
    expect(firstShuffle).not.toEqual(cards.map((c) => c.entity.id));
    expect(firstShuffle).not.toEqual(reshuffle);
  });
});
