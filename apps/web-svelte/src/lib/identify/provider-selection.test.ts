import { describe, expect, it } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import type { PluginProvider } from "$lib/api/identify-types";
import { providerCanIdentifyKind } from "./provider-selection";

function providerFor(entityKind: string): PluginProvider {
  return {
    id: "provider",
    name: "Provider",
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{ entityKind, actions: [] }],
    auth: [],
    missingAuthKeys: [],
  };
}

describe("providerCanIdentifyKind", () => {
  it("uses the definition-owned compatible plugin kind", () => {
    expect(providerCanIdentifyKind(providerFor(ENTITY_KIND.video), ENTITY_KIND.movie)).toBe(true);
  });

  it("does not invent a fallback for kinds without one", () => {
    expect(providerCanIdentifyKind(providerFor(ENTITY_KIND.video), ENTITY_KIND.videoSeries)).toBe(false);
  });

  it("still accepts direct kind support case-insensitively", () => {
    expect(providerCanIdentifyKind(providerFor("ViDeO-SeRiEs"), ENTITY_KIND.videoSeries)).toBe(true);
  });
});
