import { describe, expect, it } from "vitest";
import { PRISM_HEAT_STOPS, prismHeatColor } from "./prism-scale";

describe("prismHeatColor", () => {
  it("anchors the ends of the scale on the spectrum's bright cool and warm hues", () => {
    expect(prismHeatColor(0)).toBe("rgb(10 179 230)");
    expect(prismHeatColor(1)).toBe("rgb(255 20 31)");
  });

  it("interpolates between neighbouring stops", () => {
    // Halfway between cyan and green, the first segment of a five-stop scale.
    // cyan (10,179,230) and green (31,194,71) meet halfway at (21,187,151).
    expect(prismHeatColor(1 / 8)).toBe("rgb(21 187 151)");
  });

  it("clamps out-of-range and non-finite positions", () => {
    expect(prismHeatColor(-3)).toBe(prismHeatColor(0));
    expect(prismHeatColor(9)).toBe(prismHeatColor(1));
    expect(prismHeatColor(Number.NaN)).toBe(prismHeatColor(0));
  });

  it("emits an alpha channel only when the colour is translucent", () => {
    expect(prismHeatColor(1, 1)).toBe("rgb(255 20 31)");
    expect(prismHeatColor(1, 0.5)).toBe("rgb(255 20 31 / 0.500)");
    expect(prismHeatColor(1, 2)).toBe("rgb(255 20 31)");
  });
});

describe("PRISM_HEAT_STOPS", () => {
  it("spans the full gradient range in cool-to-warm order", () => {
    expect(PRISM_HEAT_STOPS[0]).toMatchObject({ offset: 0, color: "#0ab3e6" });
    expect(PRISM_HEAT_STOPS.at(-1)).toMatchObject({ offset: 1, color: "#ff141f" });
    expect(PRISM_HEAT_STOPS.map((stop) => stop.offset)).toEqual(
      [...PRISM_HEAT_STOPS.map((stop) => stop.offset)].sort((a, b) => a - b),
    );
  });
});
