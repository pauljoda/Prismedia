import { describe, expect, it } from "vitest";
import { PLUGIN_SEARCH_FIELD_TYPE } from "$lib/api/generated/codes";
import type { PluginSearchField } from "$lib/api/generated/model";
import {
  hasRequiredPluginSearchFields,
  pluginSearchCompatibilityTitle,
  seedPluginSearchFields,
  submittedPluginSearchFields,
} from "./plugin-search-fields";

const fields: PluginSearchField[] = [
  {
    key: "seriesTitle",
    label: "Series title",
    type: PLUGIN_SEARCH_FIELD_TYPE.text,
    required: true,
  },
  {
    key: "year",
    label: "Year",
    type: PLUGIN_SEARCH_FIELD_TYPE.year,
    required: false,
  },
];

describe("plugin search fields", () => {
  it("seeds the first text field from Entity context without hard-coding its key", () => {
    expect(seedPluginSearchFields(fields, {}, "Andor")).toEqual({ seriesTitle: "Andor", year: "" });
  });

  it("preserves entered values and strips values outside the active schema", () => {
    const values = seedPluginSearchFields(fields, { seriesTitle: "  Andor  ", year: "2022", stale: "x" }, "Ignored");

    expect(values).toEqual({ seriesTitle: "  Andor  ", year: "2022" });
    expect(submittedPluginSearchFields(fields, values)).toEqual({ seriesTitle: "Andor", year: "2022" });
  });

  it("validates required fields and supplies the legacy title envelope", () => {
    expect(hasRequiredPluginSearchFields(fields, { seriesTitle: "" })).toBe(false);
    expect(hasRequiredPluginSearchFields(fields, { seriesTitle: "Andor" })).toBe(true);
    expect(pluginSearchCompatibilityTitle(fields, { seriesTitle: "Andor" }, "Fallback")).toBe("Andor");
  });
});
