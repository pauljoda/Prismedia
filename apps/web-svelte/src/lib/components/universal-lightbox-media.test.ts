import { describe, expect, it } from "vitest";
import type { EntityCapability, EntityCapabilityImagesCapability } from "$lib/api/generated/model";
import {
  buildLightboxImageSource,
  buildLightboxVideoSources,
  isLightboxVideoCapable,
  lightboxEntityFromCard,
  type UniversalLightboxEntity,
} from "./universal-lightbox-media";
import { entityFileUrl } from "$lib/api/files";

const imageCapability = {
  kind: "images",
  supportedKinds: [],
  thumbnailUrl: "/assets/images/image-1/thumb.jpg",
  coverUrl: "/assets/images/image-1/cover.jpg",
  items: [
    { kind: "cover", path: "/assets/images/image-1/cover.jpg", mimeType: "image/jpeg" },
    { kind: "thumbnail", path: "/assets/images/image-1/thumb.jpg", mimeType: "image/jpeg" },
  ],
} satisfies EntityCapabilityImagesCapability;

const filesCapability = {
  kind: "files",
  items: [
    { role: "source", path: "/media/gallery/photo.jpg", mimeType: "image/jpeg" },
  ],
} satisfies EntityCapability;

function entity(
  overrides: Partial<UniversalLightboxEntity> = {},
): UniversalLightboxEntity {
  return {
    id: "image-1",
    kind: "image",
    title: "Photo.jpg",
    capabilities: [filesCapability, imageCapability],
    coverUrl: "/assets/images/image-1/thumb.jpg",
    isNsfw: false,
    rating: null,
    ...overrides,
  };
}

describe("universal-lightbox-media", () => {
  it("builds source-role URLs for original still images before capability assets", () => {
    expect(buildLightboxImageSource(entity())).toEqual({
      src: "/api/entities/image-1/files/source",
      role: "source",
    });
  });

  it("falls back to full, cover, and thumbnail image assets when no source role is present", () => {
    const withAssets = entity({
      capabilities: [
        {
          ...imageCapability,
          items: [
            { kind: "full", path: "/assets/images/image-1/full.jpg", mimeType: "image/jpeg" },
            ...imageCapability.items,
          ],
        },
      ],
    });

    expect(buildLightboxImageSource(withAssets)).toEqual({
      src: "/assets/images/image-1/full.jpg",
      role: "full",
    });
  });

  it("detects animated image entities from mime type, technical duration, and file extension", () => {
    expect(isLightboxVideoCapable(entity({
      capabilities: [{ kind: "files", items: [{ role: "source", path: "/media/clip.webm", mimeType: "video/webm" }] }],
    }))).toBe(true);

    expect(isLightboxVideoCapable(entity({
      capabilities: [{ kind: "technical", duration: "00:00:04", width: 640, height: 360, frameRate: null, bitRate: null, sampleRate: null, channels: null, codec: null, container: null, format: null }],
    }))).toBe(true);

    expect(isLightboxVideoCapable(entity({ title: "clip.mp4", capabilities: [] }))).toBe(true);
    expect(isLightboxVideoCapable(entity({ title: "photo.jpg", capabilities: [] }))).toBe(false);
  });

  it("builds original video sources before preview fallbacks", () => {
    expect(buildLightboxVideoSources(entity({
      title: "clip.webm",
      capabilities: [
        {
          kind: "files",
          items: [
            { role: "source", path: "/media/clip.webm", mimeType: "video/webm" },
            { role: "preview", path: "/assets/images/image-1/preview.mp4", mimeType: "video/mp4" },
          ],
        },
      ],
    }))).toEqual([
      { src: "/api/entities/image-1/files/source", type: "video/webm", quality: "original" },
      { src: "/api/entities/image-1/files/preview", type: "video/mp4", quality: "fallback" },
    ]);
  });

  it("maps thumbnail cards into lightbox entities without losing cover and flags", () => {
    const mapped = lightboxEntityFromCard({
      aspectRatio: "square",
      cover: { src: "/assets/images/image-1/thumb.jpg", alt: "Photo" },
      entity: {
        id: "image-1",
        kind: "image",
        title: "Photo",
        parentEntityId: null,
        sortOrder: null,
        capabilities: [{ kind: "flags", isFavorite: false, isNsfw: true, isOrganized: false }],
        childrenByKind: [],
        relationships: [],
      },
      fit: "cover",
      hover: { kind: "none" },
      meta: [],
    });

    expect(mapped).toMatchObject({
      id: "image-1",
      kind: "image",
      title: "Photo",
      coverUrl: "/assets/images/image-1/thumb.jpg",
      isNsfw: true,
    });
  });
});

describe("entityFileUrl", () => {
  it("builds encoded entity file URLs by role", () => {
    expect(entityFileUrl("image 1", "source")).toBe("/api/entities/image%201/files/source");
  });
});
