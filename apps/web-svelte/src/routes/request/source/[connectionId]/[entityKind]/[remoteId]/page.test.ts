import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import {
  BOOK_RENDITION,
  CONNECTION_STATUS,
  ENTITY_KIND,
  INTEGRATION_OPERATION,
  PLUGIN_CAPABILITY,
} from "$lib/api/generated/codes";
import type { ConnectionResponse, ManagedItemSnapshot } from "$lib/api/generated/model";
import Page from "./+page.svelte";

const mocks = vi.hoisted(() => ({
  isAdmin: true,
  breadcrumbs: vi.fn(() => () => {}),
  fetchConnections: vi.fn(),
  fetchManagedItem: vi.fn(),
  fetchManagerOptions: vi.fn(),
  inspectLocalLibraryAccess: vi.fn(),
  fetchManagedTracking: vi.fn(),
}));

vi.mock("$lib/stores/session.svelte", () => ({ useSession: () => mocks }));
vi.mock("$lib/stores/app-chrome.svelte", () => ({
  useAppChrome: () => ({ setBreadcrumbs: mocks.breadcrumbs }),
}));
vi.mock("$lib/nsfw/store.svelte", () => ({ useNsfw: () => ({ mode: "show" }) }));
vi.mock("$lib/api/connections", () => ({ fetchConnections: mocks.fetchConnections }));
vi.mock("$lib/api/managed-libraries", () => ({
  fetchManagedItem: mocks.fetchManagedItem,
  fetchManagerOptions: mocks.fetchManagerOptions,
  inspectLocalLibraryAccess: mocks.inspectLocalLibraryAccess,
  fetchManagedTracking: mocks.fetchManagedTracking,
}));

const connection: ConnectionResponse = {
  id: "connection-one",
  pluginId: "fixture-manager",
  name: "Radarr",
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
      operations: [INTEGRATION_OPERATION.getLibraryItem],
      entityKinds: [ENTITY_KIND.movie],
    },
    {
      kind: PLUGIN_CAPABILITY.externalManager,
      operations: [INTEGRATION_OPERATION.managerOptions],
      entityKinds: [ENTITY_KIND.movie],
    },
  ],
};
const snapshot: ManagedItemSnapshot = {
  item: {
    remoteId: "movie-1",
    entityKind: ENTITY_KIND.movie,
    title: "Arrival",
    year: 2016,
    externalIds: { tmdb: "329865" },
    monitored: true,
    profileId: "4",
    remoteFileCount: 1,
  },
  path: "/movies/Arrival",
  files: [{
    remoteId: "file-1",
    path: "/movies/Arrival/Arrival.mkv",
    sizeBytes: 1024,
    addedAt: null,
    targets: [{ remoteId: "movie-1", entityKind: ENTITY_KIND.movie, title: "Arrival" }],
  }],
  observedAt: "2026-09-16T12:00:00Z",
};

describe("connected source title route", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.isAdmin = true;
    mocks.fetchConnections.mockResolvedValue([connection]);
    mocks.fetchManagedItem.mockResolvedValue(snapshot);
    mocks.fetchManagerOptions.mockResolvedValue({ profiles: [{ id: "4", label: "HD-1080p" }], roots: [] });
    mocks.inspectLocalLibraryAccess.mockResolvedValue({
      remote: snapshot,
      files: [{
        remoteId: "file-1",
        libraryRootId: "root-one",
        localPath: "/library/Arrival/Arrival.mkv",
        isReadable: true,
        sizeMatches: true,
        problem: null,
      }],
    });
    mocks.fetchManagedTracking.mockResolvedValue([]);
    page.params = {
      connectionId: "connection-one",
      entityKind: ENTITY_KIND.movie,
      remoteId: "movie-1",
    };
    page.url = new URL("http://localhost/request/source/connection-one/movie/movie-1?identities=%7B%22tmdb%22%3A%22329865%22%7D") as unknown as typeof page.url;
  });

  afterEach(cleanup);

  it("loads the exact identity pin and restores a back-link to the source", async () => {
    render(Page);

    expect(await screen.findByRole("heading", { level: 1, name: "Arrival" })).toBeInTheDocument();
    expect(document.title).toBe("Arrival · Request · Prismedia");
    expect(mocks.fetchManagedItem).toHaveBeenCalledWith("connection-one", {
      entityKind: ENTITY_KIND.movie,
      remoteId: "movie-1",
      expectedExternalIds: { tmdb: "329865" },
    });
    await waitFor(() => expect(mocks.inspectLocalLibraryAccess).toHaveBeenCalledWith("connection-one", {
      entityKind: ENTITY_KIND.movie,
      remoteId: "movie-1",
      expectedExternalIds: { tmdb: "329865" },
    }));
    expect(await screen.findByText("1/1 readable")).toBeInTheDocument();
    expect(mocks.fetchManagedItem).toHaveBeenCalledTimes(1);
    expect(mocks.inspectLocalLibraryAccess).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("link", { name: "Back to Radarr" })).toHaveAttribute(
      "href",
      "/request?connection=connection-one&kind=movie",
    );
    expect(mocks.breadcrumbs).toHaveBeenCalledWith([
      { label: "Request", href: "/request" },
      { label: "Radarr", href: "/request?connection=connection-one&kind=movie" },
      { label: "Arrival" },
    ]);
  });

  it("loads a connected Book's selected audiobook without splitting its work identity", async () => {
    page.params = { connectionId: "connection-one", entityKind: ENTITY_KIND.book, remoteId: "OL1W" };
    page.url = new URL("http://localhost/request/source/connection-one/book/OL1W?identities=%7B%22openlibrarywork%22%3A%22OL1W%22%7D&rendition=audiobook") as typeof page.url;
    mocks.fetchConnections.mockResolvedValue([{ ...connection, effectiveCapabilities: [
      { kind: PLUGIN_CAPABILITY.connectedLibrary, operations: [INTEGRATION_OPERATION.getLibraryItem], entityKinds: [ENTITY_KIND.book] },
    ] }]);
    mocks.fetchManagedItem.mockResolvedValue({ ...snapshot, item: { ...snapshot.item, entityKind: ENTITY_KIND.book,
      remoteId: "OL1W", externalIds: { openlibrarywork: "OL1W" } }, files: [] });

    render(Page);

    await waitFor(() => expect(mocks.fetchManagedItem).toHaveBeenCalledWith("connection-one", {
      entityKind: ENTITY_KIND.book,
      remoteId: "OL1W",
      expectedExternalIds: { openlibrarywork: "OL1W" },
      bookRendition: BOOK_RENDITION.audiobook,
    }));
    expect(screen.getByRole("button", { name: "Book format" })).toHaveTextContent("Audiobook");
  });

  it("does not call administrator APIs for a non-administrator", async () => {
    mocks.isAdmin = false;
    render(Page);

    expect(await screen.findByText("Administrator access required")).toBeInTheDocument();
    expect(mocks.fetchConnections).not.toHaveBeenCalled();
    expect(mocks.fetchManagedItem).not.toHaveBeenCalled();
    expect(mocks.inspectLocalLibraryAccess).not.toHaveBeenCalled();
  });

  it("rejects a route without a non-empty exact identity pin", async () => {
    page.url = new URL("http://localhost/request/source/connection-one/movie/movie-1?identities=%7B%7D") as unknown as typeof page.url;
    render(Page);

    expect(await screen.findByText(/title link is incomplete/i)).toBeInTheDocument();
    expect(mocks.fetchConnections).not.toHaveBeenCalled();
    expect(mocks.fetchManagedItem).not.toHaveBeenCalled();
  });

  it("shows source metadata while the automatic availability check is pending", async () => {
    mocks.inspectLocalLibraryAccess.mockImplementation(() => new Promise(() => {}));
    render(Page);

    expect(await screen.findByRole("heading", { level: 1, name: "Arrival" })).toBeInTheDocument();
    expect(screen.getAllByText("Checking availability").length).toBeGreaterThan(0);
    expect(screen.getByText("/movies/Arrival")).toBeInTheDocument();
  });

  it("keeps source metadata visible when automatic availability cannot be checked", async () => {
    mocks.inspectLocalLibraryAccess.mockRejectedValue(new Error("Radarr did not respond."));
    render(Page);

    expect(await screen.findByRole("heading", { level: 1, name: "Arrival" })).toBeInTheDocument();
    expect(await screen.findByText(/could not complete the file check.*Radarr did not respond/i)).toBeInTheDocument();
    expect(screen.getByText("/movies/Arrival")).toBeInTheDocument();
  });

  it("refreshes source details and availability without allowing a late prior result to overwrite them", async () => {
    const finishes: Array<(value: { remote: ManagedItemSnapshot; files: never[] }) => void> = [];
    mocks.inspectLocalLibraryAccess.mockImplementation(() => new Promise((resolve) => {
      finishes.push(resolve);
    }));
    mocks.fetchManagedItem
      .mockResolvedValueOnce(snapshot)
      .mockResolvedValueOnce({ ...snapshot, path: "/movies/Arrival refreshed" });
    render(Page);
    await screen.findByRole("heading", { level: 1, name: "Arrival" });
    await waitFor(() => expect(mocks.inspectLocalLibraryAccess).toHaveBeenCalledTimes(1));

    await fireEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(mocks.fetchManagedItem).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(mocks.inspectLocalLibraryAccess).toHaveBeenCalledTimes(2));
    expect(screen.getByText("/movies/Arrival refreshed")).toBeInTheDocument();

    finishes[0]?.({ remote: { ...snapshot, path: "/stale/path" }, files: [] });

    await waitFor(() => expect(screen.queryByText("/stale/path")).not.toBeInTheDocument());
    expect(screen.getByText("/movies/Arrival refreshed")).toBeInTheDocument();
    finishes[1]?.({ remote: { ...snapshot, path: "/movies/Arrival refreshed" }, files: [] });
  });
});
