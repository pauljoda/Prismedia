import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import Page from "./+page.svelte";

const mocks = vi.hoisted(() => ({ isAdmin: true, canRequestContent: true, fetchConnections: vi.fn(), fetchIntegrationTransfers: vi.fn(), replaceState: vi.fn() }));
vi.mock("$lib/stores/session.svelte", () => ({ useSession: () => mocks }));
vi.mock("$lib/nsfw/store.svelte", () => ({ useNsfw: () => ({ mode: "off" }) }));
vi.mock("$lib/api/connections", () => ({ fetchConnections: mocks.fetchConnections }));
vi.mock("$lib/api/plugins", () => ({ fetchPluginProviders: async () => [] }));
vi.mock("$lib/api/settings", () => ({ fetchSettingsValues: async () => ({ values: {} }) }));
vi.mock("$lib/api/integration-transfers", () => ({ fetchIntegrationTransfers: mocks.fetchIntegrationTransfers }));
vi.mock("$app/navigation", () => ({ replaceState: mocks.replaceState }));

describe("Request workspace", () => {
  beforeEach(() => {
    vi.clearAllMocks(); mocks.isAdmin = true; mocks.canRequestContent = true;
    mocks.fetchConnections.mockResolvedValue([]); mocks.fetchIntegrationTransfers.mockResolvedValue([]);
    page.url = new URL("http://localhost/request") as unknown as typeof page.url;
    window.history.replaceState({}, "", "/request");
  });
  it("opens Activity immediately without navigation and leaves Browse ready to return to", async () => {
    render(Page);
    await screen.findByText("What would you like to find?");
    expect(mocks.fetchIntegrationTransfers).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("tab", { name: "Activity" }));
    expect(await screen.findByRole("heading", { name: "Requests & imports" })).toBeVisible();
    await waitFor(() => expect(mocks.fetchIntegrationTransfers).toHaveBeenCalledOnce());
    expect(mocks.replaceState.mock.calls[0][0].searchParams.has("activity")).toBe(true);
    await fireEvent.click(screen.getByRole("tab", { name: "Browse" }));
    expect(screen.getByText("What would you like to find?")).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Requests & imports" })).not.toBeInTheDocument();
  });
  it("does not expose connection activity or fetch admin resources to a requester", async () => {
    mocks.isAdmin = false;
    page.url = new URL("http://localhost/request?activity") as unknown as typeof page.url;
    render(Page);
    await screen.findByText("What would you like to find?");
    expect(screen.queryByRole("tab", { name: "Activity" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Sources" })).not.toBeInTheDocument();
    expect(mocks.fetchConnections).not.toHaveBeenCalled();
    expect(mocks.fetchIntegrationTransfers).not.toHaveBeenCalled();
  });
});
