import { describe, expect, it, vi, afterEach } from "vitest";
import {
  detectUiPrefsFormFactor,
  formFactorUiPrefKey,
  parseUiPrefsFormFactor,
} from "./form-factor-prefs";

describe("form-factor prefs", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("derives mobile and desktop preference keys from a base key", () => {
    expect(formFactorUiPrefKey("series:view", "mobile")).toBe("series:view:mobile");
    expect(formFactorUiPrefKey("series:view", "desktop")).toBe("series:view:desktop");
    expect(formFactorUiPrefKey("surface:images", "mobile", ":prefs")).toBe(
      "surface:images:mobile:prefs",
    );
  });

  it("parses only supported form factors", () => {
    expect(parseUiPrefsFormFactor("mobile")).toBe("mobile");
    expect(parseUiPrefsFormFactor("desktop")).toBe("desktop");
    expect(parseUiPrefsFormFactor("tablet")).toBeNull();
  });

  it("detects mobile from the shared media query", () => {
    vi.stubGlobal(
      "matchMedia",
      vi.fn().mockReturnValue({
        matches: true,
        addEventListener: () => {},
        removeEventListener: () => {},
      }),
    );

    expect(detectUiPrefsFormFactor()).toBe("mobile");
  });
});
