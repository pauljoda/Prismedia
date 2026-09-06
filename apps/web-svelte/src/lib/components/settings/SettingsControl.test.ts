import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { SettingDescriptor } from "$lib/api/settings";
import SettingsControl from "./SettingsControl.svelte";

function descriptor(overrides: Partial<SettingDescriptor>): SettingDescriptor {
  return {
    key: "playback.showCastControls",
    groupKey: "playback",
    label: "Show cast controls",
    description: "Shows the cast button in the video player.",
    type: "boolean",
    value: true,
    defaultValue: true,
    isDefault: true,
    order: 10,
    constraints: null,
    options: [],
    inputKind: null,
    applyHint: null,
    ...overrides,
  };
}

describe("SettingsControl", () => {
  it("disables both numeric step buttons when a setting is unavailable", () => {
    render(SettingsControl, {
      setting: descriptor({ type: "integer", value: 3, constraints: { min: 1, max: 5, step: 1 } }),
      disabled: true,
      onCommit: vi.fn(),
    });
    expect(screen.getByRole("button", { name: "Decrement" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Increment" })).toBeDisabled();
  });

  it("commits a keyboard slider change only once and respects its numeric bounds", async () => {
    const onCommit = vi.fn();
    render(SettingsControl, {
      setting: descriptor({ type: "decimal", value: 1, constraints: { min: 0, max: 2, step: 0.25 } }),
      onCommit,
    });
    const slider = screen.getByRole("slider", { name: "Show cast controls" });
    slider.focus();
    await fireEvent.keyDown(slider, { key: "ArrowRight" });
    await fireEvent.keyUp(slider, { key: "ArrowRight" });
    await fireEvent.blur(slider);
    expect(onCommit).toHaveBeenCalledExactlyOnceWith("playback.showCastControls", 1.25);
  });

  it("exposes one named switch without nesting interactive controls", async () => {
    const onCommit = vi.fn();
    render(SettingsControl, { setting: descriptor({ value: false }), onCommit });
    const control = screen.getByRole("switch", { name: "Show cast controls" });
    expect(control.parentElement?.closest("button")).toBeNull();
    await fireEvent.click(control);
    expect(onCommit).toHaveBeenCalledExactlyOnceWith("playback.showCastControls", true);
  });

  it("names and disables select settings at the control", () => {
    render(SettingsControl, {
      setting: descriptor({ type: "select", value: "a", options: [{ value: "a", label: "Alpha" }] }),
      disabled: true,
      onCommit: vi.fn(),
    });
    expect(screen.getByRole("button", { name: "Show cast controls" })).toBeDisabled();
  });

  it("renders boolean settings as toggle cards", async () => {
    const onCommit = vi.fn();
    render(SettingsControl, {
      props: {
        setting: descriptor({ value: false }),
        onCommit,
      },
    });

    await fireEvent.click(screen.getByText("Show cast controls"));

    expect(onCommit).toHaveBeenCalledWith("playback.showCastControls", true);
  });

  it("commits string-list settings as arrays", async () => {
    const onCommit = vi.fn();
    render(SettingsControl, {
      props: {
        setting: descriptor({
          key: "playback.audioPreferredLanguages",
          label: "Preferred audio languages",
          type: "stringList",
          value: ["en", "eng"],
          defaultValue: ["en", "eng"],
        }),
        onCommit,
      },
    });

    const input = screen.getByLabelText("Preferred audio languages") as HTMLInputElement;
    await fireEvent.input(input, { target: { value: "ja, jpn" } });
    await fireEvent.blur(input);

    expect(onCommit).toHaveBeenCalledWith("playback.audioPreferredLanguages", ["ja", "jpn"]);
  });
});
