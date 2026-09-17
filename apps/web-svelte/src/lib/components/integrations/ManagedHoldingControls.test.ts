import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ENTITY_KIND, MANAGED_CONTROL_PHASE } from "$lib/api/generated/codes";
import type { ManagedControlActionResponse, ManagedControlPreview } from "$lib/api/generated/model";
import ManagedHoldingControls from "./ManagedHoldingControls.svelte";

const api = vi.hoisted(() => ({ fetchControlActions: vi.fn(), fetchControlPreview: vi.fn(), saveControlAction: vi.fn(),
  refreshControlAction: vi.fn(), cancelControlAction: vi.fn(), closeControlAction: vi.fn() }));
vi.mock("$lib/api/managed-controls", () => ({ ...api, ManagerActionRejectedError: class extends Error {} }));
const preview: ManagedControlPreview = {
  scopeFingerprint: "a".repeat(64),
  state: {
    item: { remoteId: "1", entityKind: ENTITY_KIND.movie, title: "Film", year: null, externalIds: { tmdb: "42" }, monitored: false, profileId: "7", remoteFileCount: 0 },
    path: "/remote/film", targets: [{ target: { remoteId: "1", entityKind: ENTITY_KIND.movie, seasonNumber: null, episodeNumber: null, absoluteNumber: null }, monitored: false }],
    capabilities: { canSearch: true, canChangeProfile: true, canChangeMonitoring: true, monitoringUnavailableReason: null }, command: null,
  },
  options: { profiles: [{ id: "7", label: "Existing quality" }], roots: [] },
};
function action(values: Partial<ManagedControlActionResponse> = {}): ManagedControlActionResponse {
  return { id: "action", connectionId: "connection", holdingId: "holding", revision: 3, phase: MANAGED_CONTROL_PHASE.pendingSearch,
    changes: { profileId: null, monitored: null }, searchRequested: true, configurationConfirmed: false, command: null,
    reviewRequired: false, canCancel: true, canCloseUnverified: false, createdAt: "2026-09-16T12:00:00Z", updatedAt: "2026-09-16T12:00:00Z", problem: null, ...values };
}
async function open() {
  render(ManagedHoldingControls, { connectionId: "connection", holdingId: "holding", canPreview: true, connectionName: "Radarr" });
  await fireEvent.click(screen.getByRole("button", { name: "Radarr settings and activity" }));
  await screen.findByRole("dialog", { name: "Settings & activity · Radarr" });
  await waitFor(() => expect(api.fetchControlActions).toHaveBeenCalled());
}
describe("Manager controls", () => {
  beforeEach(() => { vi.resetAllMocks(); api.fetchControlActions.mockResolvedValue([]); api.fetchControlPreview.mockResolvedValue(preview); api.saveControlAction.mockResolvedValue(action()); });
  it("searches the reviewed scope while preserving untouched settings", async () => {
    await open();
    const submit = await screen.findByRole("button", { name: "Apply changes" });
    expect(submit).toBeDisabled();
    await fireEvent.click(screen.getByRole("checkbox", { name: "Search now" })); await fireEvent.click(submit);
    await waitFor(() => expect(api.saveControlAction).toHaveBeenCalled());
    expect(api.saveControlAction.mock.calls[0]).toEqual(["connection", "holding", expect.objectContaining({ scopeFingerprint: preview.scopeFingerprint,
      expectedPath: "/remote/film", expectedProfileId: "7", expectedMonitoring: { "1": false }, changes: { profileId: null, monitored: null }, search: true })]);
  });
  it("retries the identical accepted intent after browser response loss", async () => {
    api.saveControlAction.mockRejectedValueOnce(new Error("Connection interrupted"));
    await open();
    await fireEvent.click(await screen.findByRole("checkbox", { name: "Search now" }));
    await fireEvent.click(screen.getByRole("button", { name: "Apply changes" }));
    await screen.findByText("Connection interrupted");
    expect(screen.getByRole("checkbox", { name: "Search now" })).toBeDisabled();
    await fireEvent.click(screen.getByRole("button", { name: "Retry same action" }));
    await waitFor(() => expect(api.saveControlAction).toHaveBeenCalledTimes(2));
    expect(api.saveControlAction.mock.calls[1][2]).toEqual(api.saveControlAction.mock.calls[0][2]);
  });
  it("requires acknowledgement before closing an uncertain search and sends the reviewed revision", async () => {
    const uncertain = action({ phase: MANAGED_CONTROL_PHASE.searchUncertain, reviewRequired: true, canCancel: false, canCloseUnverified: true });
    api.fetchControlActions.mockResolvedValue([uncertain]); api.closeControlAction.mockResolvedValue(action({ phase: MANAGED_CONTROL_PHASE.closedUnverified, canCancel: false }));
    await open(); await fireEvent.click(await screen.findByRole("button", { name: "Review unresolved action" }));
    const close = screen.getByRole("button", { name: "Close with outcome unverified" }); expect(close).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Cancel unsent stage" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("checkbox", { name: "I reviewed Radarr" })); await fireEvent.click(close);
    await waitFor(() => expect(api.closeControlAction).toHaveBeenCalledWith("connection", "holding", uncertain));
  });
  it("labels completed execution as search completion without claiming available files", async () => {
    api.fetchControlActions.mockResolvedValue([action({ phase: MANAGED_CONTROL_PHASE.completed, canCancel: false })]);
    await open();
    expect(screen.getByText("Search completed")).not.toBeVisible();
    await fireEvent.click(await screen.findByRole("button", { name: /Recent activity/ }));
    await screen.findByText("Search completed");
    expect(screen.getByText(/Search completion does not confirm a download or readable file/)).toBeInTheDocument();
  });
  it("keeps closed uncertain outcomes visible above collapsed history", async () => {
    api.fetchControlActions.mockResolvedValue([
      action({ phase: MANAGED_CONTROL_PHASE.closedUnverified, reviewRequired: false, canCancel: false }),
      action({ id: "completed", phase: MANAGED_CONTROL_PHASE.completed, canCancel: false }),
    ]);
    await open();

    expect(await screen.findByText("Closed with outcome unverified")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Active and unresolved actions" })).toBeInTheDocument();
    expect(screen.getByText("Search completed")).not.toBeVisible();
  });
  it("explains a parent monitoring restriction while leaving explicit search available", async () => {
    api.fetchControlPreview.mockResolvedValue({ ...preview, state: { ...preview.state, capabilities: {
      canSearch: true, canChangeProfile: false, canChangeMonitoring: false, monitoringUnavailableReason: "Series monitoring is disabled in the connected app.",
    } } });
    await open();
    await screen.findByText("Series monitoring is disabled in the connected app.");
    expect(screen.getByRole("switch", { name: "Monitoring" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Manager profile" })).toBeDisabled();
    expect(screen.getByRole("checkbox", { name: "Search now" })).toBeEnabled();
  });
});
