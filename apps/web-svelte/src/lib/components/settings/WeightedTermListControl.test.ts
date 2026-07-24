import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SettingDescriptor } from "$lib/api/settings";
import WeightedTermListControl from "./WeightedTermListControl.svelte";

describe("WeightedTermListControl", () => {
  afterEach(cleanup);

  it("adds, weights, removes, and saves subtitle preference terms", async () => {
    const onSave = vi.fn(async () => true);
    render(WeightedTermListControl, {
      setting: preferenceSetting(),
      onSave,
    });

    await fireEvent.input(screen.getByLabelText("New preference term"), {
      target: { value: "Forced" },
    });
    await fireEvent.input(screen.getByLabelText("New term weight"), {
      target: { value: "75" },
    });
    await fireEvent.click(screen.getByRole("button", { name: "Add term" }));
    await fireEvent.click(screen.getByRole("button", { name: "Remove Eng" }));
    await fireEvent.click(screen.getByRole("button", { name: "Save preference terms" }));

    expect(onSave).toHaveBeenCalledWith("subtitles.preferredLanguages", [
      { term: "English", weight: 100 },
      { term: "Forced", weight: 75 },
    ]);
  });
});

function preferenceSetting(): SettingDescriptor {
  return {
    key: "subtitles.preferredLanguages",
    groupKey: "subtitles",
    label: "Preferred subtitle terms",
    description: "Matching terms add their weights.",
    type: "weightedTermList",
    value: [
      { term: "English", weight: 100 },
      { term: "Eng", weight: 80 },
    ],
    defaultValue: [
      { term: "English", weight: 100 },
      { term: "Eng", weight: 80 },
    ],
    isDefault: true,
    order: 20,
    constraints: {
      min: 1,
      max: 100,
      step: 1,
      minItems: 0,
      maxItems: 32,
    },
    options: [],
    inputKind: null,
    applyHint: null,
  };
}
