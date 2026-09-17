import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse } from "$lib/api/generated/model";
import ConnectedLibraryBrowser from "./ConnectedLibraryBrowser.svelte";

const mocks = vi.hoisted(() => ({
  fetchLibraryMounts: vi.fn(),
  fetchManagedLibrary: vi.fn(),
  goto: vi.fn(async (_href: string) => {}),
}));

vi.mock("$lib/api/managed-libraries", () => ({
  fetchLibraryMounts: mocks.fetchLibraryMounts,
  fetchManagedLibrary: mocks.fetchManagedLibrary,
}));
vi.mock("$app/navigation", () => ({ goto: mocks.goto }));

const connection: ConnectionResponse = {
  id: "connection-one",
  pluginId: "fixture-manager",
  name: "Existing collection",
  baseUrl: "http://manager.test/",
  enabled: true,
  enabledCapabilities: [PLUGIN_CAPABILITY.connectedLibrary, PLUGIN_CAPABILITY.externalManager],
  settings: {},
  configuredSecretKeys: [],
  revision: 1,
  status: CONNECTION_STATUS.ready,
  remoteInstanceId: null,
  hasPersistentRemoteIdentity: false,
  lastCheckedAt: null,
  lastError: null,
  effectiveCapabilities: [
    {
      kind: PLUGIN_CAPABILITY.connectedLibrary,
      operations: [INTEGRATION_OPERATION.searchLibrary, INTEGRATION_OPERATION.getLibraryItem],
      entityKinds: [ENTITY_KIND.movie],
    },
    {
      kind: PLUGIN_CAPABILITY.externalManager,
      operations: [INTEGRATION_OPERATION.managerOptions],
      entityKinds: [ENTITY_KIND.movie],
    },
  ],
};
const item = {
  remoteId: "movie/1",
  entityKind: ENTITY_KIND.movie,
  title: "A film",
  year: 2024,
  externalIds: { fixture: "identity:1", tmdb: "42" },
  monitored: false,
  profileId: "4",
  remoteFileCount: 1,
};

describe("Connected library browser", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchLibraryMounts.mockResolvedValue([]);
    mocks.fetchManagedLibrary.mockResolvedValue({ items: [item], nextCursor: null });
  });

  it("keeps browsing separate from tracking and configuration", async () => {
    render(ConnectedLibraryBrowser, { connection });

    await screen.findByText("A film");
    expect(screen.queryByText("Local library mappings")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Library settings" })).toBeInTheDocument();
  });

  it("opens a dedicated route pinned to the selected external identities", async () => {
    render(ConnectedLibraryBrowser, { connection });

    await fireEvent.click(await screen.findByRole("button", { name: "A film" }));

    expect(mocks.goto).toHaveBeenCalledOnce();
    const href = String(mocks.goto.mock.calls[0]?.[0]);
    const url = new URL(href, "http://localhost");
    expect(url.pathname).toBe("/request/source/connection-one/movie/movie%2F1");
    expect(JSON.parse(url.searchParams.get("identities") ?? "null")).toEqual({
      fixture: "identity:1",
      tmdb: "42",
    });
  });

  it("does not fetch title details inside the browser", async () => {
    render(ConnectedLibraryBrowser, { connection });

    await screen.findByText("A film");
    expect(screen.queryByRole("dialog", { name: "A film" })).not.toBeInTheDocument();
    expect(Object.hasOwn(mocks, "fetchManagedItem")).toBe(false);
  });

  it("does not turn an outage into an empty successful library", async () => {
    mocks.fetchManagedLibrary.mockRejectedValue(new Error("The manager is unavailable"));
    render(ConnectedLibraryBrowser, { connection });

    await screen.findByText("The manager is unavailable");
    expect(screen.queryByText("No matching titles")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
  });

  it("keeps exact remote availability wording in the result card", async () => {
    mocks.fetchManagedLibrary.mockResolvedValue({
      items: [{ ...item, remoteFileCount: null }],
      nextCursor: null,
    });
    render(ConnectedLibraryBrowser, { connection });

    expect((await screen.findAllByText("2024 · Availability unknown")).length).toBeGreaterThan(0);
  });

  it("opens a multi-kind library with the kind selected in Request", async () => {
    const multiKind = { ...connection, effectiveCapabilities: connection.effectiveCapabilities.map(capability =>
      capability.kind === PLUGIN_CAPABILITY.connectedLibrary ? { ...capability, entityKinds: [ENTITY_KIND.movie, ENTITY_KIND.book] } : capability) };
    const view = render(ConnectedLibraryBrowser, { connection: multiKind, initialEntityKind: ENTITY_KIND.book });

    await screen.findByText("A film");
    expect(mocks.fetchManagedLibrary).toHaveBeenCalledWith(connection.id, expect.objectContaining({ entityKind: ENTITY_KIND.book }));
    mocks.fetchManagedLibrary.mockClear();
    await view.rerender({ connection: multiKind, initialEntityKind: ENTITY_KIND.movie });
    await waitFor(() => expect(mocks.fetchManagedLibrary).toHaveBeenCalledWith(connection.id, expect.objectContaining({ entityKind: ENTITY_KIND.movie })));
  });

  it("uses real source artwork in connected-library results", async () => {
    const posterUrl = "https://images.example.test/poster.jpg";
    mocks.fetchManagedLibrary.mockResolvedValue({
      items: [{ ...item, presentation: { posterUrl } }],
      nextCursor: null,
    });
    const { container } = render(ConnectedLibraryBrowser, { connection });

    await screen.findByText("A film");
    expect(container.querySelector(`img[src="${posterUrl}"]`)).toBeInTheDocument();
  });
});
