import { describe, expect, it } from "vitest";
import { ENTITY_KIND, IDENTIFY_ACTION } from "$lib/api/generated/codes";
import type { PluginEntitySupport } from "$lib/api/generated/model";
import {
  labelForIdentifyAction,
  pluginCapabilities,
  summarizeCapabilities,
} from "./plugin-capabilities";

function support(entityKind: string, actions: string[]): PluginEntitySupport {
  return { entityKind, actions };
}

describe("labelForIdentifyAction", () => {
  it("maps wire codes to short human labels", () => {
    expect(labelForIdentifyAction(IDENTIFY_ACTION.search)).toBe("Search");
    expect(labelForIdentifyAction(IDENTIFY_ACTION.lookupId)).toBe("ID");
    expect(labelForIdentifyAction(IDENTIFY_ACTION.lookupUrl)).toBe("URL");
  });

  it("falls back to the raw value for an action it does not know", () => {
    expect(labelForIdentifyAction("lookup-isbn")).toBe("lookup-isbn");
  });
});

describe("pluginCapabilities", () => {
  it("resolves family labels and accents instead of exposing entity codes", () => {
    const [capability] = pluginCapabilities([
      support(ENTITY_KIND.videoSeries, [IDENTIFY_ACTION.search, IDENTIFY_ACTION.lookupId]),
    ]);

    expect(capability.label).not.toBe(ENTITY_KIND.videoSeries);
    expect(capability.label).toBe("Series");
    expect(capability.accent).toMatchObject({ primary: "#9e873b", secondary: "#4d925d" });
    expect(capability.searchable).toBe(true);
  });

  it("orders actions consistently regardless of the order the plugin declared them", () => {
    const [capability] = pluginCapabilities([
      support(ENTITY_KIND.movie, [IDENTIFY_ACTION.lookupUrl, IDENTIFY_ACTION.search, IDENTIFY_ACTION.lookupId]),
    ]);

    expect(capability.actionLabels).toEqual(["Search", "ID", "URL"]);
  });

  it("keeps an unrecognized action rather than dropping the capability", () => {
    const [capability] = pluginCapabilities([
      support(ENTITY_KIND.book, [IDENTIFY_ACTION.search, "lookup-isbn"]),
    ]);

    expect(capability.actionLabels).toEqual(["Search", "lookup-isbn"]);
  });

  it("sorts families along the prism spectrum", () => {
    const capabilities = pluginCapabilities([
      support(ENTITY_KIND.audioTrack, [IDENTIFY_ACTION.search]),
      support(ENTITY_KIND.video, [IDENTIFY_ACTION.search]),
      support(ENTITY_KIND.book, [IDENTIFY_ACTION.search]),
    ]);

    expect(capabilities.map((capability) => capability.entityKind)).toEqual([
      ENTITY_KIND.video,
      ENTITY_KIND.book,
      ENTITY_KIND.audioTrack,
    ]);
  });

  it("marks a family without search as not searchable", () => {
    const [capability] = pluginCapabilities([
      support(ENTITY_KIND.videoSeason, [IDENTIFY_ACTION.lookupId]),
    ]);

    expect(capability.searchable).toBe(false);
    expect(capability.actionLabels).toEqual(["ID"]);
  });
});

describe("summarizeCapabilities", () => {
  it("joins family labels for a collapsed summary", () => {
    const capabilities = pluginCapabilities([
      support(ENTITY_KIND.video, [IDENTIFY_ACTION.search]),
      support(ENTITY_KIND.book, [IDENTIFY_ACTION.search]),
    ]);

    expect(summarizeCapabilities(capabilities)).toBe("Videos, Books");
  });

  it("is empty when a plugin declares no support", () => {
    expect(summarizeCapabilities(pluginCapabilities([]))).toBe("");
  });
});
