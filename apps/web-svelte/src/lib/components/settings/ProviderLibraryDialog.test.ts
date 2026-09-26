import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import type { LibraryRoot, ProviderLibraryConnection } from "$lib/api/generated/model";
import ProviderLibraryDialog from "./ProviderLibraryDialog.svelte";

const mocks = vi.hoisted(() => ({
  fetchProviderLibraries: vi.fn(),
  saveLibraryMount: vi.fn(),
  attachExistingLibraryMount: vi.fn(),
}));

vi.mock("$lib/api/managed-libraries", () => mocks);

const root: LibraryRoot = {
  id: "root-one", path: "/media/movies", label: "Movies", enabled: true, recursive: true,
  scanVideos: true, scanImages: false, scanAudio: false, scanBooks: false, isNsfw: false,
  lastScannedAt: null, createdAt: "2026-09-18T00:00:00Z", updatedAt: "2026-09-18T00:00:00Z",
  autoIdentify: true, createdByUserId: null, accessUserIds: [], isReadOnly: false,
};

const provider: ProviderLibraryConnection = {
  connectionId: "connection-one",
  connectionName: "Radarr",
  pluginId: "radarr",
  error: null,
  libraries: [{
    remoteId: "4",
    label: "Cinema",
    remotePath: "/radarr/movies",
    entityKinds: [ENTITY_KIND.movie],
    managementUrl: "https://radarr.example/",
  }],
};

describe("Provider library dialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchProviderLibraries.mockResolvedValue([provider]);
    mocks.attachExistingLibraryMount.mockResolvedValue({});
  });

  it("attaches a discovered provider library to a compatible existing root", async () => {
    render(ProviderLibraryDialog, { roots: [root], onComplete: vi.fn(), onError: vi.fn(), onMessage: vi.fn() });

    await fireEvent.click(screen.getByRole("button", { name: "Add provider library" }));
    await screen.findByRole("dialog");
    const providerSelect = screen.getByRole("button", { name: "Provider library" });
    providerSelect.focus();
    await fireEvent.keyDown(providerSelect, { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "Radarr · Cinema · /radarr/movies" }));
    expect(screen.getByText("Provider path: /radarr/movies")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Folder visible to Prismedia" })).toBeInTheDocument();
    expect(screen.getByText(/The Radarr API reports paths and metadata; this mapping translates paths and does not transfer media files/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Shared storage example" }));
    expect(screen.getByText(/A symbolic link alone does not provide network access/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("radio", { name: "Use existing library" }));
    const rootSelect = screen.getByRole("button", { name: "Existing Prismedia library" });
    rootSelect.focus();
    await fireEvent.keyDown(rootSelect, { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "Movies · /media/movies" }));
    await fireEvent.click(screen.getByRole("button", { name: "Add library" }));

    await waitFor(() => expect(mocks.attachExistingLibraryMount).toHaveBeenCalledWith("connection-one", {
      entityKind: ENTITY_KIND.movie,
      remoteRootId: "4",
      expectedRemotePath: "/radarr/movies",
      existingLibraryRootId: "root-one",
      expectedLocalPath: "/media/movies",
    }));
    expect(mocks.saveLibraryMount).not.toHaveBeenCalled();
  });

  it("explains how to make a provider library available when none are discovered", async () => {
    mocks.fetchProviderLibraries.mockResolvedValue([]);
    render(ProviderLibraryDialog, { roots: [root], onComplete: vi.fn(), onError: vi.fn(), onMessage: vi.fn() });

    await fireEvent.click(screen.getByRole("button", { name: "Add provider library" }));

    expect(await screen.findByText(/Check that the provider connection is enabled and exposes a library/)).toBeInTheDocument();
    expect(screen.getByText(/make its media folder readable by the Prismedia server through a mount or network share/)).toBeInTheDocument();
  });

  it("reports when every discovered provider library is already linked", async () => {
    const linkedRoot: LibraryRoot = {
      ...root,
      isReadOnly: true,
      externalOrigin: {
        connectionId: provider.connectionId,
        connectionName: provider.connectionName,
        pluginId: provider.pluginId,
        remoteLibraryId: "4",
        remotePath: "/radarr/movies",
        managementUrl: "https://radarr.example/",
      },
    };
    render(ProviderLibraryDialog, { roots: [linkedRoot], onComplete: vi.fn(), onError: vi.fn(), onMessage: vi.fn() });

    await fireEvent.click(screen.getByRole("button", { name: "Add provider library" }));

    expect(await screen.findByText("Every discovered provider library is already linked.")).toBeInTheDocument();
    expect(screen.queryByText(/make its media folder readable/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Shared storage example" })).not.toBeInTheDocument();
  });
});
