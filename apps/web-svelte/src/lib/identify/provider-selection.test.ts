import { describe, expect, it } from "vitest";
import { ENTITY_KIND, PLUGIN_SEARCH_FIELD_TYPE } from "$lib/api/generated/codes";
import type { PluginEntitySupport, PluginProvider } from "$lib/api/identify-types";
import {
  providerCanIdentifyKind,
  providerSearchFieldsForKind,
  providerSupportForKind,
} from "./provider-selection";

function providerFor(
  entityKind: string,
  options: {
    actions?: string[];
    search?: PluginEntitySupport["search"];
  } = {},
): PluginProvider {
  return {
    id: "provider",
    name: "Provider",
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [
      {
        entityKind,
        actions: options.actions ?? [],
        search: options.search,
      },
    ],
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
  it("returns direct support when declared", () => {
    const provider = providerFor(ENTITY_KIND.movie);
    expect(providerSupportForKind(provider, ENTITY_KIND.movie)?.entityKind).toBe(ENTITY_KIND.movie);
  });

  it("returns fallback support when the entity kind falls back to video", () => {
    const provider = providerFor(ENTITY_KIND.video);
    expect(providerSupportForKind(provider, ENTITY_KIND.movie)?.entityKind).toBe(ENTITY_KIND.video);
  });

  it("returns null when neither direct nor fallback is supported", () => {
    const provider = providerFor(ENTITY_KIND.book);
    expect(providerSupportForKind(provider, ENTITY_KIND.movie)).toBeNull();
  });

  it("returns null when provider is undefined or null", () => {
    expect(providerSupportForKind(null, ENTITY_KIND.movie)).toBeNull();
  });
});

describe("providerSearchFieldsForKind", () => {
  it("returns declared search fields from direct support", () => {
    const fields = [
      { key: "title", label: "Title", type: PLUGIN_SEARCH_FIELD_TYPE.text, required: true },
    ];
    const provider = providerFor(ENTITY_KIND.movie, { search: { fields } });
    expect(providerSearchFieldsForKind(provider, ENTITY_KIND.movie)).toEqual(fields);
  });

  it("returns declared search fields from fallback support (movie -> video)", () => {
    const fields = [
      { key: "query", label: "Query", type: PLUGIN_SEARCH_FIELD_TYPE.text, required: true },
    ];
    const provider = providerFor(ENTITY_KIND.video, { search: { fields } });
    expect(providerSearchFieldsForKind(provider, ENTITY_KIND.movie)).toEqual(fields);
  });

  it("returns empty array when support has no declared search fields (lookup-only)", () => {
    const provider = providerFor(ENTITY_KIND.video);
    expect(providerSearchFieldsForKind(provider, ENTITY_KIND.movie)).toEqual([]);
  });

  it("returns empty array when provider does not support kind or fallback", () => {
    const provider = providerFor(ENTITY_KIND.book);
    expect(providerSearchFieldsForKind(provider, ENTITY_KIND.movie)).toEqual([]);
  });
});
