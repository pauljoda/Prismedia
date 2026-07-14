import { describe, expect, it } from "vitest";
import { paletteFromPixels } from "./artwork-palette";

type Rgb = [number, number, number];

function image(width: number, height: number, colorAt: (x: number, y: number) => Rgb): Uint8ClampedArray {
  const values: number[] = [];
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      values.push(...colorAt(x, y), 255);
    }
  }
  return new Uint8ClampedArray(values);
}

function rgb(hex: string): Rgb {
  return [1, 3, 5].map((index) => Number.parseInt(hex.slice(index, index + 2), 16)) as Rgb;
}

function luminance(hex: string): number {
  const channels = rgb(hex).map((value) => {
    const encoded = value / 255;
    return encoded <= 0.04045 ? encoded / 12.92 : ((encoded + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}

function contrast(left: string, right: string): number {
  const a = luminance(left);
  const b = luminance(right);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

describe("paletteFromPixels", () => {
  it("uses perceptual clustering to separate a cover's background from vivid accents", () => {
    const cream: Rgb = [224, 216, 190];
    const crimson: Rgb = [176, 28, 43];
    const blue: Rgb = [30, 82, 160];
    const pixels = image(12, 12, (x, y) => {
      if (x < 2 || y < 2 || x >= 10 || y >= 10) return cream;
      if (x < 8) return crimson;
      return blue;
    });

    const palette = paletteFromPixels(pixels, 12, 12);

    expect(palette).not.toBeNull();
    expect(luminance(palette!.background)).toBeLessThan(0.08);
    expect(palette!.primary).not.toBe(palette!.secondary);
    expect(contrast(palette!.primary, palette!.background)).toBeGreaterThanOrEqual(3);
    expect(contrast(palette!.secondary, palette!.background)).toBeGreaterThanOrEqual(3);
    expect(luminance(palette!.primary)).toBeLessThan(0.55);
    expect(luminance(palette!.secondary)).toBeLessThan(0.55);
    expect([palette!.primary, palette!.secondary].some((color) => {
      const [red, green, blueChannel] = rgb(color);
      return red > green * 1.35 && red > blueChannel * 1.2;
    })).toBe(true);
  });

  it("returns null when artwork contains no useful opaque color", () => {
    expect(paletteFromPixels(new Uint8ClampedArray([0, 0, 0, 0]), 1, 1)).toBeNull();
  });
});
