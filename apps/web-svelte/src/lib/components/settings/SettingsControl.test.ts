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
