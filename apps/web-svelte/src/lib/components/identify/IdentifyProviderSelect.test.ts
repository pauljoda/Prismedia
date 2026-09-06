import { fireEvent, render, screen, within, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import IdentifyProviderSelect from "./IdentifyProviderSelect.svelte";
import type { PluginProvider } from "$lib/api/identify-types";
import { ENTITY_KIND, IDENTIFY_ACTION } from "$lib/api/generated/codes";

describe("IdentifyProviderSelect", () => {
  it("names its search dialog, restores focus and does not commit on Escape", async () => {
    const onChange = vi.fn();
    render(IdentifyProviderSelect, { providers: [provider("catalog", "Catalog")], selectedId: "catalog", onChange });
    const trigger = screen.getByRole("button", { name: "Provider: Catalog" });
    trigger.focus();
    await fireEvent.click(trigger);
    expect(await screen.findByRole("dialog", { name: "Provider" })).toBeInTheDocument();
    const search = screen.getByRole("combobox", { name: "Search providers" });
    await waitFor(() => expect(search).toHaveFocus());
    await fireEvent.input(search, { target: { value: "missing" } });
    expect(await screen.findByText("No providers found")).toBeInTheDocument();
    await fireEvent.keyDown(search, { key: "Escape" });
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(onChange).not.toHaveBeenCalled();
  });

  it("filters providers by search text and commits the selected provider id", async () => {
    const onChange = vi.fn();
    render(IdentifyProviderSelect, {
      providers: [
        provider("tmdb", "The Movie Database"),
        provider("youtube", "YouTube Metadata"),
        provider("musicbrainz", "MusicBrainz"),
      ],
      selectedId: "tmdb",
      onChange,
    });

    await fireEvent.click(screen.getByRole("button", { name: "Provider: The Movie Database" }));
    await fireEvent.input(screen.getByLabelText("Search providers"), {
      target: { value: "you" },
    });

    const listbox = screen.getByRole("listbox");
    expect(within(listbox).getByText("YouTube Metadata")).toBeInTheDocument();
    expect(within(listbox).queryByText("MusicBrainz")).not.toBeInTheDocument();

    await fireEvent.click(within(listbox).getByRole("option", { name: /youtube metadata/i }));

    expect(onChange).toHaveBeenCalledWith("youtube");
    expect(onChange).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("searches by provider id with the keyboard and resets the query on reopen", async () => {
    const onChange = vi.fn();
    render(IdentifyProviderSelect, { providers: [provider("catalog", "Catalog"), provider("archive-id", "Archive")], selectedId: "catalog", onChange });
    const trigger = screen.getByRole("button", { name: "Provider: Catalog" });
    trigger.focus();
    await fireEvent.click(trigger);
    const input = screen.getByRole("combobox", { name: "Search providers" });
    await fireEvent.input(input, { target: { value: " ARCHIVE-ID " } });
    const option = await screen.findByRole("option", { name: "Archive archive-id" });
    await waitFor(() => expect(input).toHaveAttribute("aria-activedescendant", option.id));
    expect(document.getElementById(input.getAttribute("aria-controls")!)).toContainElement(option);
    await fireEvent.keyDown(input, { key: "Enter" });
    expect(onChange).toHaveBeenCalledExactlyOnceWith("archive-id");
    await waitFor(() => expect(trigger).toHaveFocus());
    await fireEvent.click(trigger);
    expect(screen.getByRole("combobox", { name: "Search providers" })).toHaveValue("");
    expect(screen.getAllByRole("option")).toHaveLength(2);
  });

  it("uses the first available provider as a fallback and disables an empty catalog", async () => {
    const { rerender } = render(IdentifyProviderSelect, { providers: [provider("catalog", "Catalog")], selectedId: "missing", onChange: vi.fn() });
    expect(screen.getByRole("button", { name: "Provider: Catalog" })).toBeEnabled();
    await rerender({ providers: [] });
    expect(screen.getByRole("button", { name: "Provider: Select provider" })).toBeDisabled();
  });
});

function provider(id: string, name: string): PluginProvider {
  return {
    id,
    name,
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{ entityKind: ENTITY_KIND.video, actions: [IDENTIFY_ACTION.search] }],
    auth: [],
    missingAuthKeys: [],
  };
}
