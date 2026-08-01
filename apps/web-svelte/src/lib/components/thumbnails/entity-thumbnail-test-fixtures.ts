import {
  CAPABILITY_KIND,
  ENTITY_KIND,
  THUMBNAIL_HOVER_KIND,
  THUMBNAIL_META_ICON,
} from "$lib/api/generated/codes";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

export function spriteCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "video-1",
      kind: ENTITY_KIND.video,
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
      kind: THUMBNAIL_HOVER_KIND.sprite,
      vttUrl: "/api/playback/videos/1/trickplay/280/tiles.m3u8",
    },
  };
}

export function imageSequenceCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "book-1",
      kind: ENTITY_KIND.book,
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
      kind: THUMBNAIL_HOVER_KIND.imageSequence,
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
      kind: ENTITY_KIND.person,
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
      kind: THUMBNAIL_HOVER_KIND.none,
    },
  };
}

export function galleryCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "gallery-2",
      kind: ENTITY_KIND.gallery,
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
      kind: THUMBNAIL_HOVER_KIND.none,
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
          kind: CAPABILITY_KIND.flags,
          isFavorite: false,
          isNsfw: true,
          isOrganized: true,
        },
        {
          kind: CAPABILITY_KIND.rating,
          value: 4,
        },
      ],
    },
    meta: [{ icon: THUMBNAIL_META_ICON.video, label: "1080p" }],
  };
}

export function bookPageCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "page-12",
      kind: ENTITY_KIND.bookPage,
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
      kind: THUMBNAIL_HOVER_KIND.none,
    },
    meta: [{ icon: THUMBNAIL_META_ICON.book, label: "Page 12" }],
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
