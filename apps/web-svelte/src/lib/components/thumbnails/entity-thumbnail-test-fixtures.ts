import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

export function spriteCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "video-1",
      kind: "video",
      title: "Video",
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "video",
    cover: {
      alt: "Video cover",
      src: "/assets/videos/1/thumb.jpg",
    },
    hover: {
      kind: "sprite",
      vttUrl: "/api/playback/videos/1/trickplay/280/tiles.m3u8",
    },
  };
}

export function imageSequenceCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "book-1",
      kind: "book",
      title: "Book",
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "poster",
    cover: null,
    hover: {
      kind: "image-sequence",
      assets: [
        { alt: "Page 1", src: "/assets/pages/1.jpg" },
        { alt: "Page 2", src: "/assets/pages/2.jpg" },
        { alt: "Page 3", src: "/assets/pages/3.jpg" },
      ],
    },
  };
}

export function personCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "person-1",
      kind: "person",
      title: "Tim Robinson",
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "portrait",
    cover: null,
    hover: {
      kind: "none",
    },
  };
}

export function galleryCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "gallery-2",
      kind: "gallery",
      title: "A secondGallery",
      parentEntityId: "gallery-1",
      sortOrder: 0,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "square",
    cover: {
      alt: "A secondGallery cover",
      src: "/assets/galleries/2/thumb.jpg",
    },
    hover: {
      kind: "none",
    },
  };
}

export function episodeCard(): EntityThumbnailCard {
  return {
    ...spriteCard(),
    custom: {
      bottomLeft: {
        label: "S1 E2",
        title: "Season 1, Episode 2",
      },
    },
    entity: {
      ...spriteCard().entity,
      capabilities: [
        {
          kind: "flags",
          isFavorite: false,
          isNsfw: true,
          isOrganized: true,
        },
        {
          kind: "rating",
          value: 4,
        },
      ],
    },
    meta: [{ icon: "video", label: "1080p" }],
  };
}

export function bookPageCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "page-12",
      kind: "book-page",
      title: "Page 12",
      parentEntityId: "chapter-1",
      sortOrder: 12,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "poster",
    cover: {
      alt: "Page 12",
      src: "/assets/pages/page-12.jpg",
    },
    hover: {
      kind: "none",
    },
    meta: [{ icon: "book", label: "Page 12" }],
  };
}

export function pointerEvent(
  type: string,
  clientX: number,
  options: { clientY?: number; pointerId?: number; pointerType?: string } = {},
) {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, "clientX", { value: clientX });
  Object.defineProperty(event, "clientY", { value: options.clientY ?? 0 });
  Object.defineProperty(event, "pointerId", { value: options.pointerId ?? 1 });
  Object.defineProperty(event, "pointerType", { value: options.pointerType ?? "mouse" });
  return event;
}
