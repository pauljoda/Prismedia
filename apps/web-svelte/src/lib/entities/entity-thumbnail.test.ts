import { describe, expect, it } from "vitest";
import {
  aspectRatioForKind,
  hasHoverPreview,
  pickHoverAsset,
  toAspectRatioValue,
  type EntityThumbnailCard,
} from "./entity-thumbnail";

const card: EntityThumbnailCard = {
  entity: {
    id: "video-1",
    kind: "video",
    title: "Sample Video",
    parentEntityId: null,
      sortOrder: null,
      relationships: [],
    capabilities: [],
    childrenByKind: [],
  },
  aspectRatio: "video",
  cover: {
    src: "/sample/cover.jpg",
    alt: "Video cover",
  },
  hover: {
    kind: "trickplay",
    assets: [
      { src: "/sample/frame-1.jpg", alt: "Frame 1" },
      { src: "/sample/frame-2.jpg", alt: "Frame 2" },
      { src: "/sample/frame-3.jpg", alt: "Frame 3" },
    ],
  },
};

describe("entity thumbnail helpers", () => {
  it("normalizes named aspect ratios", () => {
    expect(toAspectRatioValue("poster")).toBe("2 / 3");
    expect(toAspectRatioValue("video")).toBe("16 / 9");
    expect(toAspectRatioValue({ width: 4, height: 5 })).toBe("4 / 5");
  });

  it("falls back to a stable video card shape for invalid ratios", () => {
    expect(toAspectRatioValue({ width: 0, height: 5 })).toBe("16 / 9");
  });

  it("uses a wider portrait shape for people by default", () => {
    expect(aspectRatioForKind("person")).toEqual({ width: 4, height: 5 });
  });

  it("selects hover assets by pointer ratio and clamps edge values", () => {
    expect(pickHoverAsset(card, -1)?.src).toBe("/sample/frame-1.jpg");
    expect(pickHoverAsset(card, 0.5)?.src).toBe("/sample/frame-2.jpg");
    expect(pickHoverAsset(card, 3)?.src).toBe("/sample/frame-3.jpg");
  });

  it("reports hover support only when preview assets exist", () => {
    expect(hasHoverPreview(card)).toBe(true);
    expect(hasHoverPreview({ ...card, hover: { kind: "image-sequence", assets: [] } })).toBe(false);
    expect(hasHoverPreview({ ...card, hover: { kind: "none" } })).toBe(false);
  });

  it("reports sprite hover as supported without assets array", () => {
    expect(hasHoverPreview({
      ...card,
      hover: { kind: "sprite", spriteUrl: "/assets/videos/1/sprite", vttUrl: "/assets/videos/1/trickplay" },
    })).toBe(true);
  });

  it("pickHoverAsset returns null for sprite hover kind", () => {
    const spriteCard: EntityThumbnailCard = {
      ...card,
      hover: { kind: "sprite", spriteUrl: "/assets/videos/1/sprite", vttUrl: "/assets/videos/1/trickplay" },
    };
    expect(pickHoverAsset(spriteCard, 0.5)).toBeNull();
  });
});
