import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import CollectionEditor from "./CollectionEditor.svelte";

const mocks = vi.hoisted(() => ({ preview: vi.fn(), save: vi.fn() }));
vi.mock("$app/navigation", () => ({ goto: vi.fn() }));
vi.mock("$lib/nsfw/store.svelte", () => ({ useNsfw: () => ({ mode: "off" }) }));
vi.mock("$lib/stores/app-chrome.svelte", () => ({ useAppChrome: () => ({ setBreadcrumbs: () => () => {} }) }));
vi.mock("$lib/api/settings", async original => ({
  ...await original<typeof import("$lib/api/settings")>(), fetchLibraryRoots: async () => [],
}));
vi.mock("$lib/api/collections", () => ({
  previewCollectionRules: mocks.preview, createCollection: mocks.save, updateCollection: mocks.save,
}));

async function startRule() {
  render(CollectionEditor, { isNew: true });
  await fireEvent.input(screen.getByRole("textbox", { name: /^Title/ }), { target: { value: "Draft" } });
  await fireEvent.click(screen.getByRole("radio", { name: "Dynamic" }));
  await fireEvent.click(screen.getByRole("button", { name: "Add condition" }));
}

describe("collection rule preview", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.preview.mockResolvedValue({ total: 0, byType: {}, sample: [] });
  });

  it("waits for complete conditions instead of showing an empty library toolbar", async () => {
    await startRule();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
    expect(screen.queryByRole("searchbox", { name: "Search the library" })).not.toBeInTheDocument();
    expect(screen.getByText("Complete your conditions")).toBeInTheDocument();
    expect(mocks.preview).not.toHaveBeenCalled();
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "cats" } });
    await waitFor(() => expect(mocks.preview).toHaveBeenCalledOnce());
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
  });

  it("does not restore an old preview after a rule becomes incomplete", async () => {
    let finish!: (value: unknown) => void;
    mocks.preview.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    await startRule();
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "cats" } });
    await waitFor(() => expect(mocks.preview).toHaveBeenCalledOnce());
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "" } });
    finish({ total: 8, byType: {}, sample: [] });
    await waitFor(() => expect(screen.getByText("Complete your conditions")).toBeInTheDocument());
    expect(screen.queryByText("8 matching items")).not.toBeInTheDocument();
  });
});
