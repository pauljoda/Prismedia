import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, ExternalLibraryMount, PluginProvider } from "$lib/api/generated/model";
import Page from "./+page.svelte";

const mocks = vi.hoisted(() => ({
  session: { isAdmin: true },
  fetchConnections: vi.fn(),
  fetchPluginProviders: vi.fn(),
  addConnection: vi.fn(),
  saveConnection: vi.fn(),
  probeConnection: vi.fn(),
  removeConnection: vi.fn(),
  fetchLibraryMounts: vi.fn(),
  fetchManagerOptions: vi.fn(),
  saveLibraryMount: vi.fn(),
}));

vi.mock("$lib/stores/session.svelte", () => ({ useSession: () => mocks.session }));
vi.mock("$lib/api/connections", () => ({
  fetchConnections: mocks.fetchConnections,
  addConnection: mocks.addConnection,
  saveConnection: mocks.saveConnection,
  probeConnection: mocks.probeConnection,
  removeConnection: mocks.removeConnection,
}));
vi.mock("$lib/api/plugins", () => ({ fetchPluginProviders: mocks.fetchPluginProviders }));
vi.mock("$lib/api/managed-libraries", () => ({
  fetchLibraryMounts: mocks.fetchLibraryMounts,
  fetchManagerOptions: mocks.fetchManagerOptions,
  saveLibraryMount: mocks.saveLibraryMount,
}));

const catalogPlugin: PluginProvider = {
  id: "fixture", name: "Book catalog", version: "1.0.0", installed: true, enabled: true, isNsfw: false,
  supports: [], auth: [], missingAuthKeys: [], updateAvailable: false, availableVersion: null,
  integration: { protocolVersion: 1, settings: [], capabilities: [{ kind: PLUGIN_CAPABILITY.catalogDiscovery,
    operations: [INTEGRATION_OPERATION.browse], entityKinds: [ENTITY_KIND.book] }] },
};

const managerPlugin: PluginProvider = {
  ...catalogPlugin,
  id: "manager-fixture",
  name: "Library manager",
  integration: { protocolVersion: 1, settings: [], capabilities: [{ kind: PLUGIN_CAPABILITY.externalManager,
    operations: [INTEGRATION_OPERATION.managerOptions], entityKinds: [ENTITY_KIND.book] }] },
};

function connection(plugin: PluginProvider, overrides: Partial<ConnectionResponse> = {}): ConnectionResponse {
  return {
    id: `${plugin.id}-connection`, pluginId: plugin.id, name: "My connection", baseUrl: "http://connection.test", enabled: true,
    enabledCapabilities: plugin.integration?.capabilities.map(item => item.kind) ?? [], settings: {}, configuredSecretKeys: [], revision: 1,
    status: CONNECTION_STATUS.unverified, remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
    effectiveCapabilities: plugin.integration?.capabilities ?? [], ...overrides,
  };
}

function setup(plugin: PluginProvider, current: ConnectionResponse) {
  mocks.fetchConnections.mockResolvedValue([current]);
  mocks.fetchPluginProviders.mockResolvedValue([plugin]);
  mocks.fetchLibraryMounts.mockResolvedValue([]);
  mocks.saveConnection.mockResolvedValue(current);
  mocks.probeConnection.mockResolvedValue({ ...current, status: CONNECTION_STATUS.ready });
}

describe("Settings connections", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.session.isAdmin = true;
  });

  it("tests an enabled connection after saving and reports the confirmed status", async () => {
    const current = connection(catalogPlugin);
    const saved = { ...current, name: "Updated connection" };
    setup(catalogPlugin, current);
    mocks.saveConnection.mockResolvedValue(saved);
    mocks.probeConnection.mockResolvedValue({ ...saved, status: CONNECTION_STATUS.ready });

    render(Page);
    const edit = await screen.findByRole("button", { name: "Edit" });
    await fireEvent.click(edit);
    await fireEvent.input(screen.getByRole("textbox", { name: "Connection name" }), { target: { value: saved.name } });
    await fireEvent.click(screen.getByRole("button", { name: "Save connection" }));

    await waitFor(() => expect(mocks.saveConnection).toHaveBeenCalledOnce());
    expect(mocks.probeConnection).toHaveBeenCalledWith(current.id);
    expect(await screen.findByText("Connection saved and tested.")).toBeInTheDocument();
    await waitFor(() => expect(edit).toHaveFocus());
  });

  it("returns focus to the row Edit action when the editor is cancelled", async () => {
    const current = connection(catalogPlugin);
    setup(catalogPlugin, current);

    render(Page);
    const edit = await screen.findByRole("button", { name: "Edit" });
    await fireEvent.click(edit);
    await fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    await waitFor(() => expect(edit).toHaveFocus());
  });

  it("skips the automatic test when a saved connection is disabled", async () => {
    const current = connection(catalogPlugin, { enabled: false, status: CONNECTION_STATUS.disabled });
    setup(catalogPlugin, current);
    mocks.saveConnection.mockResolvedValue(current);

    render(Page);
    await fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
    await fireEvent.click(screen.getByRole("button", { name: "Save connection" }));

    await waitFor(() => expect(screen.getByText("Connection saved. Enable it and test the connection when ready.")).toBeInTheDocument());
    expect(mocks.probeConnection).not.toHaveBeenCalled();
  });

  it("keeps the saved row when the automatic test fails", async () => {
    const current = connection(catalogPlugin);
    const saved = { ...current, name: "Saved despite timeout" };
    setup(catalogPlugin, current);
    mocks.saveConnection.mockResolvedValue(saved);
    mocks.probeConnection.mockRejectedValue(new Error("timed out"));

    render(Page);
    await fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
    await fireEvent.input(screen.getByRole("textbox", { name: "Connection name" }), { target: { value: saved.name } });
    await fireEvent.click(screen.getByRole("button", { name: "Save connection" }));

    expect(await screen.findByRole("article", { name: saved.name })).toBeInTheDocument();
    expect(await screen.findByText("Connection saved. Automatic testing failed; use Test connection to retry.")).toBeInTheDocument();
  });

  it("refreshes retained folder counts after closing the mapping dialog", async () => {
    const current = connection(managerPlugin, { id: "manager-connection", name: "My manager" });
    const mount: ExternalLibraryMount = {
      id: "mount-1", connectionId: current.id, libraryRootId: "library-1", remoteRootId: "remote-1",
      remotePath: "/books", localPath: "/media/books", label: "Books",
    };
    setup(managerPlugin, current);
    mocks.fetchLibraryMounts
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([mount])
      .mockResolvedValueOnce([mount]);

    render(Page);
    expect(await screen.findByText("No library folders linked")).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Manage library folders" }));
    expect(await screen.findByRole("heading", { name: /Library folders · My manager/ })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Done" }));

    await waitFor(() => expect(screen.getByText("1 library folder linked")).toBeInTheDocument());
    expect(mocks.fetchLibraryMounts).toHaveBeenCalledTimes(3);
  });

  it("does not offer Request browsing for an inspect-only catalog capability", async () => {
    const inspectOnly = connection(catalogPlugin, {
      status: CONNECTION_STATUS.ready,
      effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.catalogDiscovery, operations: [INTEGRATION_OPERATION.inspect], entityKinds: [ENTITY_KIND.book] }],
    });
    setup(catalogPlugin, inspectOnly);

    render(Page);
    await screen.findByRole("article", { name: inspectOnly.name });
    expect(screen.queryByRole("link", { name: "Browse titles" })).not.toBeInTheDocument();
  });
});
