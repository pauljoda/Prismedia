import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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

  afterEach(() => {
    vi.useRealTimers();
  });

  it("waits for the initial tracking read before offering a new link", () => {
    api.fetchManagedTracking.mockReturnValue(new Promise(() => {}));
    render(ManagedHoldingTracking, { connectionId: "connection", item });

    expect(screen.getByText("Checking library link…")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Match existing items" })).not.toBeInTheDocument();
  });

  it("does not let a slower poll replace a newer tracking response", async () => {
    vi.useFakeTimers();
    const first = deferred<Awaited<ReturnType<typeof api.fetchManagedTracking>>>();
    const second = deferred<Awaited<ReturnType<typeof api.fetchManagedTracking>>>();
    api.fetchManagedTracking.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    render(ManagedHoldingTracking, { connectionId: "connection", item });

    await vi.advanceTimersByTimeAsync(15_000);
    second.resolve([{ ...tracked, title: "Current tracking", status: MANAGED_TRACKING_STATUS.tracking }]);
    await vi.advanceTimersByTimeAsync(0);
    expect(screen.getByText("Current tracking")).toBeInTheDocument();

    first.resolve([{ ...tracked, title: "Outdated tracking", status: MANAGED_TRACKING_STATUS.tracking }]);
    await vi.advanceTimersByTimeAsync(0);
    expect(screen.getByText("Current tracking")).toBeInTheDocument();
    expect(screen.queryByText("Outdated tracking")).not.toBeInTheDocument();
  });

  it("does not surface a slower poll failure after a newer read succeeds", async () => {
    vi.useFakeTimers();
    const first = deferred<Awaited<ReturnType<typeof api.fetchManagedTracking>>>();
    const second = deferred<Awaited<ReturnType<typeof api.fetchManagedTracking>>>();
    api.fetchManagedTracking.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    render(ManagedHoldingTracking, { connectionId: "connection", item });

    await vi.advanceTimersByTimeAsync(15_000);
    second.resolve([{ ...tracked, title: "Current tracking", status: MANAGED_TRACKING_STATUS.tracking }]);
    await vi.advanceTimersByTimeAsync(0);
    first.reject(new Error("Stale tracking failure"));
    await vi.advanceTimersByTimeAsync(0);

    expect(screen.getByText("Current tracking")).toBeInTheDocument();
    expect(screen.queryByText("Stale tracking failure")).not.toBeInTheDocument();
  });

  it("keeps independent request history loadable when tracking cannot refresh", async () => {
    api.fetchManagedTracking.mockRejectedValue(new Error("Tracking unavailable"));
    const onLoaded = vi.fn();
    render(ManagedHoldingTracking, { connectionId: "connection", onLoaded });
    await screen.findByText("Tracking unavailable");
    expect(screen.queryByText("Checking library link…")).not.toBeInTheDocument();
    expect(onLoaded).toHaveBeenCalledWith([]);
  });

  it("clears title-specific state and reloads when the selected identity changes", async () => {
    const nextItem = { ...item, externalIds: { tmdb: "2" }, title: "Another film" };
    const nextRead = deferred<Awaited<ReturnType<typeof api.fetchManagedTracking>>>();
    api.fetchManagedTracking
      .mockResolvedValueOnce([{ ...tracked, title: "First title", status: MANAGED_TRACKING_STATUS.tracking }])
      .mockReturnValueOnce(nextRead.promise);
    const view = render(ManagedHoldingTracking, { connectionId: "connection", item });
    await screen.findByText("First title");

    await view.rerender({ connectionId: "connection", item: nextItem });
    expect(await screen.findByText("Checking library link…")).toBeInTheDocument();
    expect(screen.queryByText("First title")).not.toBeInTheDocument();

    nextRead.resolve([{
      ...tracked,
      title: "Second title",
      status: MANAGED_TRACKING_STATUS.tracking,
      item: { ...tracked.item, expectedExternalIds: nextItem.externalIds },
    }]);
    expect(await screen.findByText("Second title")).toBeInTheDocument();
  });

  it("requires reviewing exact local matches before persisting the association", async () => {
    render(ManagedHoldingTracking, { connectionId: "connection", item });
    expect(api.saveManagedTracking).not.toHaveBeenCalled();
    await fireEvent.click(await screen.findByRole("button", { name: "Match existing items" }));
    await screen.findByText("/library/film.mkv");
    expect(api.saveManagedTracking).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Link existing items" }));
    await screen.findByText("Waiting for verification");
    expect(api.saveManagedTracking).toHaveBeenCalledWith("connection", expect.objectContaining({ libraryRootId: "mapped-root", selections: [selection], item: tracked.item }));
  });

  it("keeps ambiguous or unscanned coverage out of the linking action", async () => {
    api.previewTracking.mockResolvedValue({ ...preview, reviewReason: "Scan the existing source first." });
    render(ManagedHoldingTracking, { connectionId: "connection", item });
    await fireEvent.click(await screen.findByRole("button", { name: "Match existing items" }));
    await screen.findByText("Scan the existing source first.");
    expect(screen.queryByRole("button", { name: "Link existing items" })).not.toBeInTheDocument();
  });

  it("reuses the operation ID after a submission response is lost", async () => {
    api.saveManagedTracking.mockRejectedValueOnce(new Error("Response lost"));
    render(ManagedHoldingTracking, { connectionId: "connection", item });
    await fireEvent.click(await screen.findByRole("button", { name: "Match existing items" }));
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
  it("does not reuse tracking when a manager recycles a remote ID for another external identity", async () => {
    api.fetchManagedTracking.mockResolvedValue([{ ...tracked, title: "Old identity", item: { ...tracked.item, expectedExternalIds: { tmdb: "999" } } }]);
    render(ManagedHoldingTracking, { connectionId: "connection", item });
    await screen.findByRole("button", { name: "Match existing items" });
    expect(screen.queryByText("Old identity")).not.toBeInTheDocument();
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

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((onResolve, onReject) => {
    resolve = onResolve;
    reject = onReject;
  });
  return { promise, resolve, reject };
}
