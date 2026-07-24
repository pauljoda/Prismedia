import { describe, expect, it, vi } from "vitest";
import { fetchLibraryConfig, fetchSettingsValues, updateSetting } from "./settings";

describe("settings API", () => {
  it("normalizes setting values from generated responses", async () => {
    const fetchMock = mockFetch({
      values: {
        "scan.intervalMinutes": "30",
        "subtitles.preferredLanguages": [
          { term: "Forced", weight: "80" },
          { term: "English", weight: 55 },
        ],
      },
    });

    const response = await fetchSettingsValues(["scan.intervalMinutes", "subtitles.preferredLanguages"]);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/settings/values?keys=scan.intervalMinutes&keys=subtitles.preferredLanguages",
      expect.anything(),
    );
    expect(response.values).toEqual({
      "scan.intervalMinutes": "30",
      "subtitles.preferredLanguages": [
        { term: "Forced", weight: 80 },
        { term: "English", weight: 55 },
      ],
    });
  });

  it("normalizes setting descriptors after update", async () => {
    mockFetch({
      key: "playback.showCastControls",
      groupKey: "playback",
      label: "Show cast controls",
      description: "Shows cast controls.",
      type: "boolean",
      value: true,
      defaultValue: false,
      isDefault: false,
      order: "10",
      constraints: {
        min: "1",
        max: "100",
        step: "1",
        minItems: null,
        maxItems: null,
      },
      options: [],
      inputKind: null,
      applyHint: null,
    });

    const setting = await updateSetting("playback.showCastControls", true);

    expect(setting.order).toBe(10);
    expect(setting.value).toBe(true);
    expect(setting.constraints?.max).toBe(100);
  });

  it("normalizes library config settings and preserves roots", async () => {
    mockFetch({
      settings: {
        groups: [{
          key: "playback",
          label: "Playback",
          order: "1",
          settings: [{
            key: "playback.showCastControls",
            groupKey: "playback",
            label: "Show cast controls",
            description: "Shows cast controls.",
            type: "boolean",
            value: true,
            defaultValue: true,
            isDefault: true,
            order: "1",
            constraints: null,
            options: [],
            inputKind: null,
            applyHint: null,
          }],
        }],
      },
      roots: [{ id: "root-1", label: "Movies", path: "/media/movies", enabled: true }],
    });

    const config = await fetchLibraryConfig();

    expect(config.settings.groups[0].order).toBe(1);
    expect(config.roots[0].path).toBe("/media/movies");
  });
});

function mockFetch(data: unknown) {
  const fetchMock = vi.fn(async () => new Response(JSON.stringify(data), {
    headers: { "Content-Type": "application/json" },
    status: 200,
  }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}
