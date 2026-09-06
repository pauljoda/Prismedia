import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { COLLECTION_RULE_FIELD as FIELD, ENTITY_KIND } from "$lib/api/generated/codes";
import RuleEntityPicker from "./RuleEntityPicker.svelte";

const mocks = vi.hoisted(() => ({ search: vi.fn(), thumbnails: vi.fn() }));
vi.mock("$lib/entities/entity-picker-search", () => ({ searchEntityPickerItems: mocks.search }));
vi.mock("$lib/api/entities", () => ({ fetchEntityThumbnails: mocks.thumbnails }));
vi.mock("$lib/nsfw/store.svelte", () => ({ useNsfw: () => ({ mode: "off" }) }));
const seriesId = "00000000-0000-4000-8000-000000000001";

describe("collection Entity rule picker", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.thumbnails.mockResolvedValue([]);
    mocks.search.mockResolvedValue([]);
  });

  it.each([
    [FIELD.tags, ENTITY_KIND.tag, "Tags"],
    [FIELD.performers, ENTITY_KIND.person, "People"],
    [FIELD.studio, ENTITY_KIND.studio, "Studios"],
  ])("selects complete names for %s without splitting commas", async (field, kind, label) => {
    mocks.search.mockResolvedValue([{ id: "entity-id", title: "One, Two", thumbnailUrl: "/cover.jpg" }]);
    const onChange = vi.fn();
    render(RuleEntityPicker, { field, value: [], multiple: true, onChange });
    await fireEvent.click(screen.getByRole("button", { name: `Add ${label}` }));
    await waitFor(() => expect(mocks.search).toHaveBeenCalledWith(kind, "", { hideNsfw: true }));
    await fireEvent.click((await screen.findByText("One, Two")).closest('[role="option"]')!);
    expect(onChange).toHaveBeenLastCalledWith(["One, Two"]);
  });

  it("keeps future names without creating an Entity", async () => {
    const onChange = vi.fn();
    render(RuleEntityPicker, { field: FIELD.tags, value: [], multiple: true, onChange });
    await fireEvent.click(screen.getByRole("button", { name: "Add Tags" }));
    await fireEvent.input(screen.getByRole("combobox", { name: "Search Tags" }), { target: { value: "Future tag" } });
    await fireEvent.click((await screen.findByText('Add "Future tag"')).closest('[role="option"]')!);
    expect(onChange).toHaveBeenLastCalledWith(["Future tag"]);
  });

  it("stores a selected series ID rather than its title", async () => {
    mocks.search.mockResolvedValue([{ id: seriesId, title: "Example series", thumbnailUrl: null }]);
    const onChange = vi.fn();
    render(RuleEntityPicker, { field: FIELD.videoSeriesId, value: "", multiple: false, onChange });
    await fireEvent.click(screen.getByRole("button", { name: /Series:/ }));
    await fireEvent.click((await screen.findByText("Example series")).closest('[role="option"]')!);
    expect(onChange).toHaveBeenLastCalledWith(seriesId);
  });

  it("reuses chosen artwork instead of reloading a newly selected series", async () => {
    mocks.search.mockResolvedValue([{ id: seriesId, title: "Example series", thumbnailUrl: "/thumb.jpg" }]);
    const onChange = vi.fn();
    const props = { field: FIELD.videoSeriesId, value: "", multiple: false, onChange };
    const { rerender } = render(RuleEntityPicker, props);
    await fireEvent.click(screen.getByRole("button", { name: /Series:/ }));
    await fireEvent.click((await screen.findByText("Example series")).closest('[role="option"]')!);
    await rerender({ ...props, value: seriesId });
    expect(screen.getByRole("button", { name: "Series: Example series" })).toBeInTheDocument();
    expect(mocks.thumbnails).not.toHaveBeenCalled();
  });

  it("hydrates saved series IDs for display without rewriting the rule", async () => {
    mocks.thumbnails.mockResolvedValue([{ id: seriesId, kind: ENTITY_KIND.videoSeries, title: "Saved series", coverThumbUrl: "/small.jpg" }]);
    const onChange = vi.fn();
    render(RuleEntityPicker, { field: FIELD.videoSeriesId, value: [seriesId, "Legacy title"], multiple: true, onChange });
    expect(await screen.findByText("Saved series")).toBeInTheDocument();
    expect(screen.getByText("Legacy title")).toBeInTheDocument();
    expect(mocks.thumbnails).toHaveBeenCalledWith([seriesId], expect.objectContaining({ hideNsfw: true }));
    expect(onChange).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Remove Saved series" }));
    expect(onChange).toHaveBeenLastCalledWith(["Legacy title"]);
  });

  it("retains unavailable saved references and offers a search retry", async () => {
    mocks.search.mockRejectedValueOnce(new Error("Offline")).mockResolvedValue([]);
    const onChange = vi.fn();
    render(RuleEntityPicker, { field: FIELD.tags, value: ["Saved tag"], multiple: true, onChange });
    await fireEvent.click(screen.getByRole("button", { name: "Add Tags" }));
    expect(await screen.findByText("Offline")).toBeInTheDocument();
    await fireEvent.click(screen.getByText("Retry"));
    await waitFor(() => expect(mocks.search).toHaveBeenCalledTimes(2));
    expect(screen.getByText("Saved tag")).toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("resolves differently cased saved IDs without normalizing their stored value", async () => {
    const savedId = "00000000-ABCD-4000-8000-000000000001";
    mocks.thumbnails.mockResolvedValue([{ id: savedId.toLowerCase(), kind: ENTITY_KIND.videoSeries, title: "Resolved title" }]);
    const onChange = vi.fn();
    render(RuleEntityPicker, { field: FIELD.videoSeriesId, value: savedId, multiple: false, onChange });
    expect(await screen.findByText("Resolved title")).toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("ignores saved-name lookup responses after the selection changes", async () => {
    let finish!: (items: unknown[]) => void;
    mocks.thumbnails.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    const props = { field: FIELD.videoSeriesId, value: [seriesId], multiple: true, onChange: vi.fn() };
    const { rerender } = render(RuleEntityPicker, props);
    await waitFor(() => expect(mocks.thumbnails).toHaveBeenCalledOnce());
    await rerender({ ...props, value: [] });
    finish([{ id: seriesId, kind: ENTITY_KIND.videoSeries, title: "Old series" }]);
    await waitFor(() => expect(screen.queryByText("Saved series")).not.toBeInTheDocument());
    expect(screen.queryByText("Old series")).not.toBeInTheDocument();
    expect(props.onChange).not.toHaveBeenCalled();
  });
});
