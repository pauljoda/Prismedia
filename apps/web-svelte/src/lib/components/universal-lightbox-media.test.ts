import { describe, expect, it } from "vitest";
import type { EntityCapability, EntityCapabilityImagesCapability } from "$lib/api/generated/model";
import {
  buildLightboxImageSource,
  buildLightboxPreloadSources,
  buildLightboxVideoSources,
  isAnimatedStillImage,
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

  it("falls back to cover then thumbnail image assets when no source role is present", () => {
    const withAssets = entity({
      capabilities: [imageCapability],
    });

    expect(buildLightboxImageSource(withAssets)).toEqual({
      src: "/assets/images/image-1/cover.jpg",
      role: "cover",
    });
  });

  it("detects video-capable image entities from mime type, technical duration, and file extension", () => {
    expect(isLightboxVideoCapable(entity({
      capabilities: [{ kind: "files", items: [{ role: "source", path: "/media/clip.webm", mimeType: "video/webm" }] }],
    }))).toBe(true);

    expect(isLightboxVideoCapable(entity({
      capabilities: [{ kind: "technical", duration: "00:00:04", width: 640, height: 360, frameRate: null, bitRate: null, sampleRate: null, channels: null, codec: null, container: null, format: null }],
    }))).toBe(true);

    expect(isLightboxVideoCapable(entity({ title: "clip.mp4", capabilities: [] }))).toBe(true);
    expect(isLightboxVideoCapable(entity({ title: "photo.jpg", capabilities: [] }))).toBe(false);
  });

  it("keeps animated still images on the image path even when they have duration metadata", () => {
    const gif = entity({
      title: "loop.gif",
      capabilities: [
        { kind: "files", items: [{ role: "source", path: "/media/loop.gif", mimeType: "image/gif" }] },
        {
          kind: "technical",
          duration: "00:00:04",
          width: 640,
          height: 360,
          frameRate: null,
          bitRate: null,
          sampleRate: null,
          channels: null,
          codec: null,
          container: null,
          format: "gif",
        },
      ],
    });

    expect(isAnimatedStillImage(gif)).toBe(true);
    expect(isLightboxVideoCapable(gif)).toBe(false);
  });

  it("recognizes APNG still animation from the source extension", () => {
    const apng = entity({
      title: "spark.png",
      capabilities: [
        { kind: "files", items: [{ role: "source", path: "/media/spark.apng", mimeType: "image/png" }] },
      ],
    });

    expect(isAnimatedStillImage(apng)).toBe(true);
    expect(isLightboxVideoCapable(apng)).toBe(false);
  });

  it("uses generated previews, not original sources, for image-entity animated playback", () => {
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
      { src: "/api/entities/image-1/files/preview", type: "video/mp4", quality: "fallback" },
    ]);
  });

  it("can prefer original sources for full-quality lightbox playback", () => {
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
    }), { preferOriginal: true })).toEqual([
      { src: "/api/entities/image-1/files/source", type: "video/webm", quality: "original" },
      { src: "/api/entities/image-1/files/preview", type: "video/mp4", quality: "fallback" },
    ]);
  });

  it("keeps original direct sources available for true video entities", () => {
    expect(buildLightboxVideoSources(entity({
      kind: "video",
      title: "clip.webm",
      capabilities: [
        {
          kind: "files",
          items: [
            { role: "source", path: "/media/clip.webm", mimeType: "video/webm" },
            { role: "preview", path: "/assets/videos/video-1/preview.mp4", mimeType: "video/mp4" },
          ],
        },
      ],
    }))).toEqual([
      { src: "/api/entities/image-1/files/preview", type: "video/mp4", quality: "fallback" },
      { src: "/api/entities/image-1/files/source", type: "video/webm", quality: "original" },
    ]);
  });

  it("builds nearby preload hints without pulling full true-video sources", () => {
    const previous = entity({ id: "previous", title: "Previous.jpg" });
    const current = entity({ id: "current", title: "Current.jpg" });
    const animated = entity({
      id: "animated",
      title: "clip.webm",
      coverUrl: "/assets/images/animated/thumb.jpg",
      capabilities: [
        {
          kind: "files",
          items: [
            { role: "source", path: "/media/clip.webm", mimeType: "video/webm" },
            { role: "preview", path: "/assets/images/animated/preview.mp4", mimeType: "video/mp4" },
          ],
        },
      ],
    });
    const movie = entity({
      id: "movie",
      kind: "video",
      title: "Movie.mp4",
      coverUrl: "/assets/videos/movie/poster.jpg",
      capabilities: [
        {
          kind: "files",
          items: [{ role: "source", path: "/media/movie.mp4", mimeType: "video/mp4" }],
        },
      ],
    });

    expect(buildLightboxPreloadSources([previous, current, animated, movie], 1, {
      preferOriginal: true,
      radius: 2,
    })).toEqual([
      { src: "/api/entities/previous/files/source", rel: "preload", as: "image" },
      { src: "/assets/images/animated/thumb.jpg", rel: "preload", as: "image" },
      { src: "/api/entities/animated/files/preview", rel: "prefetch", as: "video" },
      { src: "/assets/videos/movie/poster.jpg", rel: "preload", as: "image" },
    ]);
  });

  it("maps thumbnail cards into lightbox entities without losing cover, ratio, and flags", () => {
    const mapped = lightboxEntityFromCard({
      aspectRatio: { width: 1080, height: 1920 },
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
      initialAspectRatio: { width: 1080, height: 1920 },
      isNsfw: true,
    });
  });
});

describe("entityFileUrl", () => {
  it("builds encoded entity file URLs by role", () => {
    expect(entityFileUrl("image 1", "source")).toBe("/api/entities/image%201/files/source");
  });
});
