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
});
