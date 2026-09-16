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
    capabilities: { canSearch: true, canChangeProfile: true, canChangeMonitoring: true }, command: null,
  },
  options: { profiles: [{ id: "7", label: "Existing quality" }], roots: [] },
};
function action(values: Partial<ManagedControlActionResponse> = {}): ManagedControlActionResponse {
  return { id: "action", connectionId: "connection", holdingId: "holding", revision: 3, phase: MANAGED_CONTROL_PHASE.pendingSearch,
    changes: { profileId: null, monitored: null }, searchRequested: true, configurationConfirmed: false, command: null,
    reviewRequired: false, canCancel: true, canCloseUnverified: false, createdAt: "2026-09-16T12:00:00Z", updatedAt: "2026-09-16T12:00:00Z", problem: null, ...values };
}
async function open() {
  render(ManagedHoldingControls, { connectionId: "connection", holdingId: "holding", canPreview: true });
  await fireEvent.click(screen.getByRole("button", { name: "Manager controls" }));
  await waitFor(() => expect(api.fetchControlActions).toHaveBeenCalled());
}
describe("Manager controls", () => {
  beforeEach(() => { vi.resetAllMocks(); api.fetchControlActions.mockResolvedValue([]); api.fetchControlPreview.mockResolvedValue(preview); api.saveControlAction.mockResolvedValue(action()); });
  it("searches the reviewed scope while preserving untouched settings", async () => {
    await open(); await fireEvent.click(screen.getByRole("button", { name: "Review manager settings" }));
    const submit = await screen.findByRole("button", { name: "Apply manager action" });
    expect(submit).toBeDisabled();
    await fireEvent.click(screen.getByRole("checkbox", { name: "Search now" })); await fireEvent.click(submit);
    await waitFor(() => expect(api.saveControlAction).toHaveBeenCalled());
    expect(api.saveControlAction.mock.calls[0]).toEqual(["connection", "holding", expect.objectContaining({ scopeFingerprint: preview.scopeFingerprint,
      expectedPath: "/remote/film", expectedProfileId: "7", expectedMonitoring: { "1": false }, changes: { profileId: null, monitored: null }, search: true })]);
  });
  it("retries the identical accepted intent after browser response loss", async () => {
    api.saveControlAction.mockRejectedValueOnce(new Error("Connection interrupted"));
    await open(); await fireEvent.click(screen.getByRole("button", { name: "Review manager settings" }));
    await fireEvent.click(await screen.findByRole("checkbox", { name: "Search now" }));
    await fireEvent.click(screen.getByRole("button", { name: "Apply manager action" }));
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
    await fireEvent.click(screen.getByRole("checkbox", { name: "I reviewed the connected app" })); await fireEvent.click(close);
    await waitFor(() => expect(api.closeControlAction).toHaveBeenCalledWith("connection", "holding", uncertain));
  });
  it("labels completed execution as search completion without claiming available files", async () => {
    api.fetchControlActions.mockResolvedValue([action({ phase: MANAGED_CONTROL_PHASE.completed, canCancel: false })]);
    await open(); await screen.findByText("Search completed");
    expect(screen.getByText(/Search completion does not mean a download or local file is available/)).toBeInTheDocument();
  });
});
