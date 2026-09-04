import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { ENTITY_FILE_ROLE } from "$lib/entities/entity-codes";
import EntityDetailArtworkEditor from "./EntityDetailArtworkEditor.svelte";

const assets = [
  { role: ENTITY_FILE_ROLE.poster, label: "Poster", hasAsset: true },
  { role: ENTITY_FILE_ROLE.backdrop, label: "Header", hasAsset: false },
];

describe("EntityDetailArtworkEditor", () => {
  it("separates artwork status from labeled actions and explains immediate saving", async () => {
    const onClear = vi.fn();
    render(EntityDetailArtworkEditor, { assets, onUpload: vi.fn(), onClear });
    await fireEvent.click(screen.getByRole("button", { name: "Edit artwork" }));
    expect(screen.getByText("Artwork changes save immediately.")).toBeInTheDocument();
    expect(screen.getByText("No image")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Replace poster" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remove header" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Remove poster" }));
    expect(onClear).toHaveBeenCalledWith(ENTITY_FILE_ROLE.poster);
  });

  it("disables every artwork action while one image is being updated", async () => {
    render(EntityDetailArtworkEditor, { assets, busyRole: ENTITY_FILE_ROLE.poster, onUpload: vi.fn(), onClear: vi.fn() });
    await fireEvent.click(screen.getByRole("button", { name: "Edit artwork" }));
    for (const button of screen.getAllByRole("button", { name: /^(Replace|Remove|Upload) / })) expect(button).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent("Updating poster");
  });

  it("opens the native picker and routes its file to the selected role", async () => {
    const onUpload = vi.fn();
    render(EntityDetailArtworkEditor, { assets, onUpload, onClear: vi.fn() });
    await fireEvent.click(screen.getByRole("button", { name: "Edit artwork" }));
    const input = screen.getByLabelText("Artwork file") as HTMLInputElement;
    const picker = vi.spyOn(input, "click");
    await fireEvent.click(screen.getByRole("button", { name: "Upload header" }));
    expect(picker).toHaveBeenCalledOnce();
    const file = new File(["image"], "header.png", { type: "image/png" });
    await fireEvent.change(input, { target: { files: [file] } });
    expect(onUpload).toHaveBeenCalledWith(ENTITY_FILE_ROLE.backdrop, file);
    expect(input.value).toBe("");
  });
});
