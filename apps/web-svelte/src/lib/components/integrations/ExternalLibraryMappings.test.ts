import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, ExternalLibraryMount, LibraryRoot } from "$lib/api/generated/model";
import ExternalLibraryMappings from "./ExternalLibraryMappings.svelte";

const mocks = vi.hoisted(() => ({
  fetchLibraryMounts: vi.fn(),
  fetchManagerOptions: vi.fn(),
  fetchLibraryRoots: vi.fn(),
  saveLibraryMount: vi.fn(),
  attachExistingLibraryMount: vi.fn(),
}));

vi.mock("$lib/api/managed-libraries", () => mocks);
vi.mock("$lib/api/settings", () => ({ fetchLibraryRoots: mocks.fetchLibraryRoots }));

const connection: ConnectionResponse = {
  id: "connection-one", pluginId: "fixture-manager", name: "Radarr", baseUrl: "http://manager.test/",
  enabled: true, enabledCapabilities: [PLUGIN_CAPABILITY.externalManager], settings: {}, configuredSecretKeys: [], revision: 1,
  status: CONNECTION_STATUS.ready, remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
  effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.externalManager, operations: [INTEGRATION_OPERATION.managerOptions], entityKinds: [ENTITY_KIND.movie] }],
};

const libraryRoot: LibraryRoot = {
  id: "library-one", path: "/media/movies", label: "Movies", enabled: false, recursive: true,
  scanVideos: true, scanImages: false, scanAudio: false, scanBooks: false, isNsfw: false,
  lastScannedAt: "2026-09-18T00:00:00Z", createdAt: "2026-09-17T00:00:00Z", updatedAt: "2026-09-17T00:00:00Z", isReadOnly: false,
};

const mount: ExternalLibraryMount = {
  id: "mount-one", connectionId: connection.id, libraryRootId: libraryRoot.id, remoteRootId: "remote-one",
  remotePath: "/radarr/movies", localPath: libraryRoot.path, label: libraryRoot.label,
};

describe("External library mappings", () => {
  afterEach(async () => {
    cleanup();
    // DialogBase releases its shared body scroll lock asynchronously after unmount.
    await waitFor(() => expect(document.body.style.overflow).not.toBe("hidden"));
  });

  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchLibraryMounts.mockResolvedValue([]);
    mocks.fetchManagerOptions.mockResolvedValue({ profiles: [], roots: [{ id: "remote-one", path: "/radarr/movies", accessible: true }] });
    mocks.fetchLibraryRoots.mockResolvedValue([libraryRoot]);
    mocks.saveLibraryMount.mockResolvedValue(mount);
    mocks.attachExistingLibraryMount.mockResolvedValue(mount);
  });

  it("links a reviewed paused library without creating a new root", async () => {
    render(ExternalLibraryMappings, { connection, kind: ENTITY_KIND.movie });

    await fireEvent.click(screen.getByRole("button", { name: "Map library folder" }));
    await screen.findByRole("dialog");
    await fireEvent.click(screen.getByRole("radio", { name: "Use existing library" }));
    const existingLibrary = screen.getByRole("button", { name: "Existing Prismedia library" });
    existingLibrary.focus();
    await fireEvent.keyDown(existingLibrary, { key: "ArrowDown" });
    await screen.findByRole("listbox");
    await fireEvent.pointerUp(screen.getByRole("option", { name: "Movies · /media/movies" }));
    await fireEvent.click(screen.getByRole("button", { name: "Link existing library" }));

    await waitFor(() => expect(mocks.attachExistingLibraryMount).toHaveBeenCalledWith(connection.id, {
      entityKind: ENTITY_KIND.movie,
      remoteRootId: "remote-one",
      expectedRemotePath: "/radarr/movies",
      existingLibraryRootId: libraryRoot.id,
      expectedLocalPath: libraryRoot.path,
    }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(mocks.saveLibraryMount).not.toHaveBeenCalled();
  });

  it("only offers compatible writable roots that are not already mapped", async () => {
    mocks.fetchLibraryRoots.mockResolvedValue([
      libraryRoot,
      { ...libraryRoot, id: "books", path: "/media/books", label: "Books", scanVideos: false, scanBooks: true },
      { ...libraryRoot, id: "external", path: "/media/external", label: "External", isReadOnly: true },
    ]);
    mocks.fetchLibraryMounts.mockResolvedValue([{ ...mount, libraryRootId: libraryRoot.id }]);

    render(ExternalLibraryMappings, { connection, kind: ENTITY_KIND.movie });
    await fireEvent.click(screen.getByRole("button", { name: "Map library folder" }));
    await screen.findByRole("dialog");
    await fireEvent.click(screen.getByRole("radio", { name: "Use existing library" }));
    const existingLibrary = screen.getByRole("button", { name: "Existing Prismedia library" });
    existingLibrary.focus();
    await fireEvent.keyDown(existingLibrary, { key: "ArrowDown" });
    await screen.findByRole("listbox");

    expect(screen.queryByRole("option", { name: "Movies · /media/movies" })).not.toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Books · /media/books" })).not.toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "External · /media/external" })).not.toBeInTheDocument();
    await fireEvent.keyDown(screen.getByRole("listbox"), { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("listbox")).not.toBeInTheDocument());
    await fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
});
