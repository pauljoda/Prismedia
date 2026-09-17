import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ENTITY_KIND, MANAGED_TRACKING_STATUS } from "$lib/api/generated/codes";
import type { ManagedReleasePreview } from "$lib/api/generated/model";
import ManagedHoldingRelease from "./ManagedHoldingRelease.svelte";
import { ManagedReleaseRejectedError } from "$lib/api/managed-release";

const api = vi.hoisted(() => ({ fetchReleasePreview: vi.fn(), saveOwnershipRelease: vi.fn() }));
vi.mock("$lib/api/managed-release", () => ({ ...api, ManagedReleaseRejectedError: class extends Error {} }));
const preview: ManagedReleasePreview = { revision: 3, scopeFingerprint: "a".repeat(64), canRelease: true, problem: null,
  observation: { queueEmpty: true, commandsIdle: true, state: {
    item: { remoteId: "1", entityKind: ENTITY_KIND.movie, title: "Film", year: null, externalIds: { tmdb: "42" }, monitored: false, profileId: "7", remoteFileCount: 1 },
    path: "/remote/film", targets: [{ target: { remoteId: "1", entityKind: ENTITY_KIND.movie, seasonNumber: null, episodeNumber: null, absoluteNumber: null }, monitored: false }],
    capabilities: { canSearch: true, canChangeProfile: true, canChangeMonitoring: true, monitoringUnavailableReason: null }, command: null,
  } } };
const accepted = { id: "holding", status: MANAGED_TRACKING_STATUS.releasePending };

describe("Managed ownership handoff", () => {
  beforeEach(() => { vi.resetAllMocks(); api.fetchReleasePreview.mockResolvedValue(preview); api.saveOwnershipRelease.mockResolvedValue(accepted); });
  async function open(onaccepted = vi.fn()) {
    render(ManagedHoldingRelease, { connectionId: "connection", holdingId: "holding", connectionName: "Radarr", onaccepted });
    await fireEvent.click(screen.getByRole("button", { name: "Stop managing this title with Radarr" }));
    await screen.findByRole("dialog", { name: "Stop managing with Radarr?" });
    await screen.findByText("Monitoring is off for 1 selected item.");
    return onaccepted;
  }
  it("requires an explicit review and acknowledgement before accepting a release", async () => {
    const saved = await open();
    const submit = screen.getByRole("button", { name: "Stop Prismedia management" });
    expect(submit).toBeDisabled(); expect(api.saveOwnershipRelease).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("checkbox", { name: "I will keep this title unmonitored in Radarr" }));
    await fireEvent.click(submit);
    await waitFor(() => expect(saved).toHaveBeenCalledWith(accepted));
    expect(api.saveOwnershipRelease).toHaveBeenCalledWith("connection", "holding", expect.objectContaining({ expectedRevision: 3,
      scopeFingerprint: preview.scopeFingerprint, expectedPath: "/remote/film" }));
  });
  it("shows remote activity and cannot release an unready scope", async () => {
    api.fetchReleasePreview.mockResolvedValue({ ...preview, canRelease: false, problem: "Downloads are still active.", observation: { ...preview.observation, queueEmpty: false } });
    await open(); await screen.findByText("Downloads are still active.");
    expect(screen.queryByRole("button", { name: "Release acquisition owner" })).not.toBeInTheDocument();
    expect(api.saveOwnershipRelease).not.toHaveBeenCalled();
  });
  it("replays identical intent after response loss without creating another handoff", async () => {
    api.saveOwnershipRelease.mockRejectedValueOnce(new Error("Response lost"));
    await open();
    await fireEvent.click(screen.getByRole("checkbox", { name: "I will keep this title unmonitored in Radarr" }));
    await fireEvent.click(screen.getByRole("button", { name: "Stop Prismedia management" }));
    await screen.findByText("Response lost");
    await fireEvent.click(screen.getByRole("button", { name: "Retry same stop request" }));
    await waitFor(() => expect(api.saveOwnershipRelease).toHaveBeenCalledTimes(2));
    expect(api.saveOwnershipRelease.mock.calls[1][2]).toEqual(api.saveOwnershipRelease.mock.calls[0][2]);
  });
  it("permits a fresh review after a definite refusal", async () => {
    api.saveOwnershipRelease.mockRejectedValueOnce(new ManagedReleaseRejectedError("Review changed"));
    await open();
    await fireEvent.click(screen.getByRole("checkbox", { name: "I will keep this title unmonitored in Radarr" }));
    await fireEvent.click(screen.getByRole("button", { name: "Stop Prismedia management" }));
    await screen.findByText("Review changed");
    expect(screen.queryByRole("button", { name: "Retry same stop request" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Check again" }));
    await screen.findByText("Monitoring is off for 1 selected item.");
    expect(api.fetchReleasePreview).toHaveBeenCalledTimes(2);
  });
});
