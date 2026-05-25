import { describe, expect, it } from "vitest";
import { assertDevRouteVisible, shouldExposeDevRoutes } from "./dev-routes";

describe("dev route visibility", () => {
  it("exposes dev routes only while the SvelteKit dev flag is active", () => {
    expect(shouldExposeDevRoutes(true)).toBe(true);
    expect(shouldExposeDevRoutes(false)).toBe(false);
  });

  it("returns a not found error for dev routes outside development builds", () => {
    expect(() => assertDevRouteVisible(false)).toThrow(
      expect.objectContaining({ status: 404 }),
    );
  });
});
