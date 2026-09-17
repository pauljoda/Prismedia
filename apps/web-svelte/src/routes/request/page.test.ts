import { fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse } from "$lib/api/generated/model";
import Page from "./+page.svelte";

const mocks = vi.hoisted(() => ({ isAdmin: true, canRequestContent: true, fetchConnections: vi.fn(), fetchConnectionCatalog: vi.fn(), fetchIntegrationTransfers: vi.fn(), fetchManagedRequests: vi.fn(), fetchManagedTracking: vi.fn(), setBreadcrumbs: vi.fn((_items: Array<{ label: string; href?: string }>) => () => {}), goto: vi.fn(), afterNavigate: vi.fn() }));
vi.mock("$lib/stores/session.svelte", () => ({ useSession: () => mocks }));
vi.mock("$lib/stores/app-chrome.svelte", () => ({ useAppChrome: () => ({ setBreadcrumbs: mocks.setBreadcrumbs }) }));
vi.mock("$lib/nsfw/store.svelte", () => ({ useNsfw: () => ({ mode: "off" }) }));
vi.mock("$lib/api/connections", () => ({ fetchConnections: mocks.fetchConnections, fetchConnectionCatalog: mocks.fetchConnectionCatalog }));
vi.mock("$lib/api/plugins", () => ({ fetchPluginProviders: async () => [] }));
vi.mock("$lib/api/settings", () => ({ fetchSettingsValues: async () => ({ values: {} }), fetchLibraryRoots: async () => [] }));
vi.mock("$lib/api/integration-transfers", () => ({ fetchIntegrationTransfers: mocks.fetchIntegrationTransfers }));
vi.mock("$lib/api/managed-requests", () => ({ fetchManagedRequests: mocks.fetchManagedRequests }));
vi.mock("$lib/api/managed-libraries", () => ({ fetchManagedTracking: mocks.fetchManagedTracking }));
vi.mock("$app/navigation", () => ({ goto: mocks.goto, afterNavigate: mocks.afterNavigate }));

describe("Request workspace", () => {
  beforeEach(() => {
    vi.clearAllMocks(); mocks.isAdmin = true; mocks.canRequestContent = true;
    mocks.fetchConnections.mockResolvedValue([]); mocks.fetchIntegrationTransfers.mockResolvedValue([]); mocks.fetchManagedRequests.mockResolvedValue([]); mocks.fetchManagedTracking.mockResolvedValue([]);
    mocks.fetchConnectionCatalog.mockResolvedValue({ title: "Books", items: [], nextCursor: null });
    page.url = new URL("http://localhost/request") as unknown as typeof page.url;
    window.history.replaceState({}, "", "/request");
  });
  it("opens Activity immediately without navigation and leaves Browse ready to return to", async () => {
    render(Page);
    await screen.findByText("What would you like to find?");
    expect(mocks.fetchIntegrationTransfers).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("tab", { name: "Activity" }));
    expect(await screen.findByRole("heading", { name: "Request activity" })).toBeVisible();
    await waitFor(() => expect(mocks.fetchIntegrationTransfers).toHaveBeenCalledOnce());
    expect(mocks.goto.mock.calls[0][0].searchParams.has("activity")).toBe(true);
    expect(mocks.goto.mock.calls[0][1]).toEqual(expect.objectContaining({ replaceState: true }));
    await fireEvent.click(screen.getByRole("tab", { name: "Browse" }));
    expect(screen.getByText("What would you like to find?")).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Request activity" })).not.toBeInTheDocument();
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

  it("keeps optimistic kind and source navigation, then resets through the same-path Request crumb", async () => {
    const source: ConnectionResponse = {
      id: "books", pluginId: "fixture", name: "Reading collection", baseUrl: "http://books.test", enabled: true,
      enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery], settings: {}, configuredSecretKeys: [], revision: 1,
      status: CONNECTION_STATUS.ready, remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
      effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.catalogDiscovery, operations: [INTEGRATION_OPERATION.browse], entityKinds: [ENTITY_KIND.book] }],
    };
    mocks.fetchConnections.mockResolvedValue([source]);
    render(Page);
    await screen.findByText("What would you like to find?");

    await fireEvent.click(screen.getByRole("button", { name: "Books" }));
    expect(mocks.goto.mock.calls.at(-1)?.[0].searchParams.get("kind")).toBe("book");
    const sourceTrigger = await screen.findByRole("button", { name: "Source" });
    await fireEvent.keyDown(sourceTrigger, { key: "ArrowDown" });
    await fireEvent.pointerUp(within(await screen.findByRole("listbox")).getByRole("option", { name: /Reading collection/i }));
    expect(mocks.goto.mock.calls.at(-1)?.[0].searchParams.get("connection")).toBe(source.id);
    expect(mocks.goto.mock.calls.at(-1)?.[1]).toEqual(expect.objectContaining({ replaceState: false }));
    await waitFor(() => expect(mocks.fetchConnectionCatalog).toHaveBeenCalledWith(source.id, expect.objectContaining({ entityKind: ENTITY_KIND.book })));

    await screen.findByText("Books");
    await waitFor(() => expect(mocks.setBreadcrumbs).toHaveBeenLastCalledWith([
      { label: "Request", href: "/request" }, { label: source.name },
    ]));

    const requestCrumb = mocks.setBreadcrumbs.mock.calls.at(-1)?.[0][0];
    expect(requestCrumb).toEqual({ label: "Request", href: "/request" });
    await fireEvent.click(screen.getByRole("button", { name: "Back to Request" }));
    await waitFor(() => expect(mocks.setBreadcrumbs).toHaveBeenLastCalledWith([{ label: "Request" }]));
    expect(await screen.findByText("What would you like to find?")).toBeInTheDocument();
  });

  it("restores a same-path source view when browser history restores its query", async () => {
    const source: ConnectionResponse = {
      id: "books", pluginId: "fixture", name: "Reading collection", baseUrl: "http://books.test", enabled: true,
      enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery], settings: {}, configuredSecretKeys: [], revision: 1,
      status: CONNECTION_STATUS.ready, remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
      effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.catalogDiscovery, operations: [INTEGRATION_OPERATION.browse], entityKinds: [ENTITY_KIND.book] }],
    };
    mocks.fetchConnections.mockResolvedValue([source]);
    render(Page);
    await screen.findByText("What would you like to find?");

    const historyUrl = new URL(`http://localhost/request?kind=book&connection=${source.id}`);
    const handleNavigate = mocks.afterNavigate.mock.calls[0]?.[0];
    expect(handleNavigate).toBeTypeOf("function");
    handleNavigate({ to: { url: historyUrl }, type: "popstate" });

    await waitFor(() => expect(mocks.fetchConnectionCatalog).toHaveBeenCalledWith(
      source.id,
      expect.objectContaining({ entityKind: ENTITY_KIND.book }),
    ));
    expect(await screen.findByText("Books")).toBeInTheDocument();
    await waitFor(() => expect(mocks.setBreadcrumbs).toHaveBeenLastCalledWith([
      { label: "Request", href: "/request" }, { label: source.name },
    ]));
  });

});
