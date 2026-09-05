import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import CollectionEditor from "./CollectionEditor.svelte";
import { personCard } from "../thumbnails/entity-thumbnail-test-fixtures";

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
    mocks.preview.mockReset().mockResolvedValue({ total: 0, byType: {}, sample: [] });
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

  it("labels a partial sample and omits controls that would only search that sample", async () => {
    mocks.preview.mockResolvedValue({ total: 50, byType: {}, sample: [{ entity: personCard().entity }] });
    await startRule();
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "cats" } });
    expect(await screen.findByRole("region", { name: "Preview sample · 1 of 50" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Tim Robinson" })).toBeInTheDocument();
    expect(screen.queryByRole("searchbox", { name: "Search the library" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Filters" })).not.toBeInTheDocument();
  });

  it("clears obsolete matches during debounce and distinguishes loading from no results", async () => {
    mocks.preview.mockResolvedValueOnce({ total: 1, byType: {}, sample: [{ entity: personCard().entity }] });
    await startRule();
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "cats" } });
    expect(screen.queryByText("No matching items")).not.toBeInTheDocument();
    await screen.findByRole("region", { name: "Preview · 1 matching item" });
    mocks.preview.mockReturnValueOnce(new Promise(() => {}));
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "dogs" } });
    expect(screen.queryByRole("link", { name: "Tim Robinson" })).not.toBeInTheDocument();
    expect(screen.queryByText("1 matching item")).not.toBeInTheDocument();
    expect(screen.getByText("Building preview")).toBeInTheDocument();
  });

  it("retries failed previews without presenting failure as an empty collection", async () => {
    mocks.preview.mockRejectedValueOnce(new Error("Preview unavailable"));
    await startRule();
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "cats" } });
    expect(await screen.findByText("Preview unavailable")).toBeInTheDocument();
    expect(screen.queryByText("No matching items")).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Retry preview" }));
    expect(await screen.findByText("No matching items")).toBeInTheDocument();
    expect(mocks.preview).toHaveBeenCalledTimes(2);
  });

  it("cancels obsolete preview requests when rules change and when leaving the editor", async () => {
    mocks.preview.mockReturnValue(new Promise(() => {}));
    const { unmount } = render(CollectionEditor, { isNew: true });
    await fireEvent.click(screen.getByRole("radio", { name: "Dynamic" }));
    await fireEvent.click(screen.getByRole("button", { name: "Add condition" }));
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "cats" } });
    await waitFor(() => expect(mocks.preview).toHaveBeenCalledOnce());
    const firstSignal = mocks.preview.mock.calls[0][1]?.signal;
    expect(firstSignal).toBeInstanceOf(AbortSignal);
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "dogs" } });
    expect(firstSignal.aborted).toBe(true);
    await waitFor(() => expect(mocks.preview).toHaveBeenCalledTimes(2));
    const secondSignal = mocks.preview.mock.calls[1][1].signal;
    expect(secondSignal.aborted).toBe(false);
    unmount();
    expect(secondSignal.aborted).toBe(true);
  });

  it("does not claim there are no matches when sample cards are unavailable", async () => {
    mocks.preview.mockResolvedValue({ total: 5, byType: {}, sample: [] });
    await startRule();
    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "cats" } });
    expect(await screen.findByText("No preview artwork available")).toBeInTheDocument();
    expect(screen.getByText("5 matching items")).toBeInTheDocument();
    expect(screen.queryByText("No matching items")).not.toBeInTheDocument();
  });
});
