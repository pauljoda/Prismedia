import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ENTITY_KIND, MANAGED_TRACKING_STATUS } from "$lib/api/generated/codes";
import ManagedHoldingTracking from "./ManagedHoldingTracking.svelte";

const api = vi.hoisted(() => ({ fetchManagedTracking: vi.fn(), previewTracking: vi.fn(), saveManagedTracking: vi.fn(), refreshTracking: vi.fn() }));
vi.mock("$lib/api/managed-libraries", () => api);
const item = { remoteId: "1", entityKind: ENTITY_KIND.movie, title: "A film", year: 2024, externalIds: { tmdb: "1" }, monitored: false, profileId: null, remoteFileCount: 1 };
const selection = { remoteTargetId: "1", entityId: "local-item", sourceFileId: "local-file" };
const preview = { libraryRootId: "mapped-root", selections: [selection], sources: [{ ...selection, localPath: "/library/film.mkv", kind: ENTITY_KIND.movie, seasonNumber: null, episodeNumber: null, absoluteNumber: null }], reviewReason: null };
const tracked = { id: "tracking", connectionId: "connection", libraryRootId: "mapped-root", item: { entityKind: item.entityKind, remoteId: item.remoteId, expectedExternalIds: item.externalIds },
  title: item.title, status: MANAGED_TRACKING_STATUS.pending, revision: 1, lastCheckedAt: null, problem: null, bindings: [] };

describe("Managed holding tracking", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.fetchManagedTracking.mockResolvedValue([]);
    api.previewTracking.mockResolvedValue(preview);
    api.saveManagedTracking.mockResolvedValue(tracked);
  });

  it("keeps independent request history loadable when tracking cannot refresh", async () => {
    api.fetchManagedTracking.mockRejectedValue(new Error("Tracking unavailable"));
    const onLoaded = vi.fn();
    render(ManagedHoldingTracking, { connectionId: "connection", onLoaded });
    await screen.findByText("Tracking unavailable");
    expect(onLoaded).toHaveBeenCalledWith([]);
  });

  it("requires reviewing exact local matches before persisting the association", async () => {
    render(ManagedHoldingTracking, { connectionId: "connection", item });
    expect(api.saveManagedTracking).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Match existing items" }));
    await screen.findByText("/library/film.mkv");
    expect(api.saveManagedTracking).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Link existing items" }));
    await screen.findByText("Waiting for verification");
    expect(api.saveManagedTracking).toHaveBeenCalledWith("connection", expect.objectContaining({ libraryRootId: "mapped-root", selections: [selection], item: tracked.item }));
  });

  it("keeps ambiguous or unscanned coverage out of the linking action", async () => {
    api.previewTracking.mockResolvedValue({ ...preview, reviewReason: "Scan the existing source first." });
    render(ManagedHoldingTracking, { connectionId: "connection", item });
    await fireEvent.click(screen.getByRole("button", { name: "Match existing items" }));
    await screen.findByText("Scan the existing source first.");
    expect(screen.queryByRole("button", { name: "Link existing items" })).not.toBeInTheDocument();
  });

  it("reuses the operation ID after a submission response is lost", async () => {
    api.saveManagedTracking.mockRejectedValueOnce(new Error("Response lost"));
    render(ManagedHoldingTracking, { connectionId: "connection", item });
    await fireEvent.click(screen.getByRole("button", { name: "Match existing items" }));
    await fireEvent.click(await screen.findByRole("button", { name: "Link existing items" }));
    await screen.findByText("Response lost");
    await fireEvent.click(screen.getByRole("button", { name: "Link existing items" }));
    await waitFor(() => expect(api.saveManagedTracking).toHaveBeenCalledTimes(2));
    expect(api.saveManagedTracking.mock.calls[0]?.[1].operationId).toBe(api.saveManagedTracking.mock.calls[1]?.[1].operationId);
  });

  it("shows retained tracking while the remote connection is unavailable", async () => {
    api.fetchManagedTracking.mockResolvedValue([{ ...tracked, status: MANAGED_TRACKING_STATUS.stale, problem: "Connection unavailable; bindings retained." }]);
    render(ManagedHoldingTracking, { connectionId: "connection" });
    await screen.findByText("Connection unavailable; bindings retained.");
    expect(screen.getByRole("button", { name: "Refresh tracking" })).toBeInTheDocument();
    expect(api.previewTracking).not.toHaveBeenCalled();
  });
  it("keeps ownership visibly reserved while a handoff is pending", async () => {
    api.fetchManagedTracking.mockResolvedValue([{ ...tracked, status: MANAGED_TRACKING_STATUS.releasePending }]);
    render(ManagedHoldingTracking, { connectionId: "connection", item, canRelease: true });
    await screen.findByText("Handoff pending");
    expect(screen.getByText("Ownership reserved until verification completes")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh handoff" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Match existing items" })).not.toBeInTheDocument();
  });
  it("retains released history and requires new review before linking the same files again", async () => {
    api.fetchManagedTracking.mockResolvedValue([{ ...tracked, status: MANAGED_TRACKING_STATUS.released }]);
    render(ManagedHoldingTracking, { connectionId: "connection", item });
    await screen.findByText("No longer managed");
    expect(screen.getByText("Files and history retained")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Refresh tracking" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Match existing items" })).toBeInTheDocument();
    expect(api.saveManagedTracking).not.toHaveBeenCalled();
  });
});
