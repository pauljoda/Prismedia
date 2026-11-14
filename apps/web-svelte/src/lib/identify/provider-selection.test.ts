import { describe, expect, it } from "vitest";
import { ENTITY_KIND, PLUGIN_SEARCH_FIELD_TYPE } from "$lib/api/generated/codes";
import type { PluginSearchField } from "$lib/api/generated/model";
import type { PluginProvider } from "$lib/api/identify-types";
import { providerCanIdentifyKind, providerSupportForKind } from "./provider-selection";

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

describe("providerSupportForKind", () => {
  const titleField: PluginSearchField = {
    key: "title",
    label: "Title",
    type: PLUGIN_SEARCH_FIELD_TYPE.text,
    required: false,
  };

  it("returns the fallback entry, with its search fields, when the kind has no direct entry", () => {
    const provider = providerFor(ENTITY_KIND.video);
    provider.supports[0]!.search = { fields: [titleField] };

    expect(providerSupportForKind(provider, ENTITY_KIND.movie)?.search?.fields).toEqual([titleField]);
  });

  it("prefers a direct entry over the fallback", () => {
    const provider = providerFor(ENTITY_KIND.video);
    provider.supports.push({ entityKind: ENTITY_KIND.movie, actions: ["direct"] });

    expect(providerSupportForKind(provider, ENTITY_KIND.movie)?.actions).toEqual(["direct"]);
  });
});
