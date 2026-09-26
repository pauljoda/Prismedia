import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ACQUISITION_ACCESS_KIND, CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse } from "$lib/api/generated/model";
import ConnectionCatalogBrowser from "./ConnectionCatalogBrowser.svelte";

const api = vi.hoisted(() => ({ fetchConnectionCatalog: vi.fn(), fetchLibraryRoots: vi.fn(), acquirePublication: vi.fn(), requestSourcePublication: vi.fn() }));
vi.mock("$lib/api/connections", () => api);
vi.mock("$lib/api/settings", () => api);
vi.mock("$lib/api/integration-transfers", () => api);
const connection: ConnectionResponse = {
  id: "source", name: "Book source", pluginId: "fixture", baseUrl: "http://source.test", enabled: true,
  enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery], settings: {}, configuredSecretKeys: [], revision: 1,
  status: CONNECTION_STATUS.ready, remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
  effectiveCapabilities: [
    { kind: PLUGIN_CAPABILITY.catalogDiscovery, operations: [INTEGRATION_OPERATION.browse, INTEGRATION_OPERATION.search], entityKinds: [ENTITY_KIND.book] },
    { kind: PLUGIN_CAPABILITY.acquisitionSource, operations: [INTEGRATION_OPERATION.resolve], entityKinds: [ENTITY_KIND.book] },
  ],
};
const item = { id: "book", selectionToken: "pinned-selection", entityKind: ENTITY_KIND.book, isContainer: false,
  publication: { title: "A book", authors: ["An author"], description: "A description", externalIds: {} },
  offers: [{ id: "epub", label: "EPUB", access: ACQUISITION_ACCESS_KIND.download, mediaType: "application/epub+zip" }],
};

describe("Connected source browsing", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.fetchConnectionCatalog.mockResolvedValue({ title: "Books", items: [item], nextCursor: null });
    api.fetchLibraryRoots.mockResolvedValue([{ id: "library", label: "Reading", enabled: true, scanBooks: true, isReadOnly: false }]);
    api.acquirePublication.mockResolvedValue({ id: "import", title: "A book" });
    api.requestSourcePublication.mockResolvedValue({ id: "request", title: "A book" });
  });

  it("shows media first and asks for a destination only after choosing a title", async () => {
    render(ConnectionCatalogBrowser, { connection });
    const title = await screen.findByRole("button", { name: "A book" });
    expect(screen.queryByRole("button", { name: "Import destination" })).not.toBeInTheDocument();
    expect(api.acquirePublication).not.toHaveBeenCalled();
    await fireEvent.click(title);
    expect(await screen.findByRole("dialog")).toHaveTextContent("A book");
    expect(screen.getByRole("button", { name: "Import destination" })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Import EPUB" }));
    await waitFor(() => expect(api.acquirePublication).toHaveBeenCalledWith("source", expect.objectContaining({ selectionToken: "pinned-selection", offerId: "epub", libraryRootId: "library" })));
    expect(await screen.findByRole("link", { name: "Follow progress in Activity" })).toHaveAttribute("href", "/request?activity");
  });

  it("does not turn borrowing into an import action", async () => {
    api.fetchConnectionCatalog.mockResolvedValue({ title: "Books", items: [{ ...item, offers: [{ ...item.offers[0], access: ACQUISITION_ACCESS_KIND.borrow }] }], nextCursor: null });
    render(ConnectionCatalogBrowser, { connection });
    await fireEvent.click(await screen.findByRole("button", { name: "A book" }));
    await screen.findByText("This source does not offer a direct download for this item.");
    expect(screen.queryByRole("button", { name: /Import EPUB/ })).not.toBeInTheDocument();
    expect(api.acquirePublication).not.toHaveBeenCalled();
  });

  it("reuses the accepted intent when an import response is lost", async () => {
    api.acquirePublication.mockRejectedValueOnce(new Error("Response lost"));
    render(ConnectionCatalogBrowser, { connection });
    await fireEvent.click(await screen.findByRole("button", { name: "A book" }));
    await fireEvent.click(screen.getByRole("button", { name: "Import EPUB" }));
    await screen.findByText("Response lost");
    await fireEvent.click(screen.getByRole("button", { name: "Import EPUB" }));
    await waitFor(() => expect(api.acquirePublication).toHaveBeenCalledTimes(2));
    expect(api.acquirePublication.mock.calls[1]).toEqual(api.acquirePublication.mock.calls[0]);
  });

  it("opens a multi-kind catalog with the kind selected in Request", async () => {
    const multiKind = { ...connection, effectiveCapabilities: [{ ...connection.effectiveCapabilities[0]!, entityKinds: [ENTITY_KIND.musicArtist, ENTITY_KIND.book] }] };
    const view = render(ConnectionCatalogBrowser, { connection: multiKind, initialEntityKind: ENTITY_KIND.book });

    await screen.findByRole("button", { name: "A book" });
    expect(api.fetchConnectionCatalog).toHaveBeenCalledWith(connection.id, expect.objectContaining({ entityKind: ENTITY_KIND.book }));
    api.fetchConnectionCatalog.mockClear();
    await view.rerender({ connection: multiKind, initialEntityKind: ENTITY_KIND.musicArtist });
    await waitFor(() => expect(api.fetchConnectionCatalog).toHaveBeenCalledWith(connection.id, expect.objectContaining({ entityKind: ENTITY_KIND.musicArtist })));
  });

  it("searches the selected source and preserves the way back to its parent", async () => {
    api.fetchConnectionCatalog.mockResolvedValueOnce({ title: "Sources", canSearch: false,
      items: [{ ...item, id: "section", selectionToken: "section-token", isContainer: true, publication: { ...item.publication, title: "Selected source" }, offers: [] }] });
    api.fetchConnectionCatalog.mockResolvedValue({ title: "Selected source", canSearch: true, items: [item] });
    render(ConnectionCatalogBrowser, { connection });
    await fireEvent.click(await screen.findByRole("button", { name: "Selected source" }));
    await fireEvent.input(await screen.findByRole("textbox", { name: "Search source" }), { target: { value: "a title" } });
    await fireEvent.click(screen.getByRole("button", { name: "Search" }));
    await waitFor(() => expect(api.fetchConnectionCatalog).toHaveBeenLastCalledWith("source", expect.objectContaining({ container: "section-token", query: "a title" })));
    await waitFor(() => expect(screen.queryByText("Loading titles")).not.toBeInTheDocument());
    await fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByRole("heading", { name: "Sources" })).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: "Search source" })).not.toBeInTheDocument();
  });

  it("requests preparation only through negotiated source capabilities and reuses uncertain intent", async () => {
    api.fetchConnectionCatalog.mockResolvedValue({ title: "Books", items: [{ ...item, offers: [{ ...item.offers[0], access: ACQUISITION_ACCESS_KIND.request }] }] });
    const prepared = { ...connection, effectiveCapabilities: [connection.effectiveCapabilities[0]!, {
      ...connection.effectiveCapabilities[1]!, operations: [INTEGRATION_OPERATION.resolve, INTEGRATION_OPERATION.requestSource, INTEGRATION_OPERATION.observeSource],
    }] };
    api.requestSourcePublication.mockRejectedValueOnce(new Error("Response lost"));
    render(ConnectionCatalogBrowser, { connection: prepared });
    await fireEvent.click(await screen.findByRole("button", { name: "A book" }));
    expect(screen.getByText(/saves a verified copy/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Request & import EPUB" }));
    await screen.findByText("Response lost");
    await fireEvent.click(screen.getByRole("button", { name: "Request & import EPUB" }));
    await waitFor(() => expect(api.requestSourcePublication).toHaveBeenCalledTimes(2));
    expect(api.requestSourcePublication.mock.calls[1]).toEqual(api.requestSourcePublication.mock.calls[0]);
    expect(api.acquirePublication).not.toHaveBeenCalled();
  });

  it("hides source request actions when the connection cannot prepare the offer", async () => {
    api.fetchConnectionCatalog.mockResolvedValue({ title: "Books", items: [{ ...item, offers: [{ ...item.offers[0], access: ACQUISITION_ACCESS_KIND.request }] }] });
    render(ConnectionCatalogBrowser, { connection });
    await fireEvent.click(await screen.findByRole("button", { name: "A book" }));
    expect(screen.queryByRole("button", { name: /Request & import/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Import destination" })).not.toBeInTheDocument();
  });
});
