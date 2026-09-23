import { fireEvent, render, screen } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  CONNECTION_STATUS,
  ENTITY_KIND,
  INTEGRATION_OPERATION,
  PLUGIN_CAPABILITY,
} from "$lib/api/generated/codes";
import type { ConnectionResponse, ManagedItemSnapshot } from "$lib/api/generated/model";
import ManagedHoldingDetail from "./ManagedHoldingDetail.test-harness.svelte";

const mocks = vi.hoisted(() => ({
  fetchLibraryMounts: vi.fn(),
  fetchManagedTracking: vi.fn(),
}));
vi.mock("$lib/api/managed-libraries", async (importOriginal) => ({
  ...await importOriginal<typeof import("$lib/api/managed-libraries")>(),
  fetchLibraryMounts: mocks.fetchLibraryMounts,
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
const snapshot: ManagedItemSnapshot = {
  item: {
    remoteId: "1",
    entityKind: ENTITY_KIND.movie,
    title: "Arrival",
    year: 2016,
    externalIds: { tmdb: "329865" },
    monitored: true,
    profileId: "4",
    remoteFileCount: 1,
  },
  path: "/movies/Arrival (2016)",
  files: [{
    remoteId: "file-1",
    path: "/movies/Arrival (2016)/Arrival.mkv",
    sizeBytes: 1024,
    addedAt: null,
    targets: [{
      remoteId: "1",
      entityKind: ENTITY_KIND.movie,
      title: "Arrival",
      seasonNumber: null,
      episodeNumber: null,
      absoluteNumber: null,
    }],
  }],
  observedAt: "2026-09-16T12:00:00Z",
};

describe("Managed holding detail", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchLibraryMounts.mockResolvedValue([]);
    mocks.fetchManagedTracking.mockResolvedValue([]);
  });

  it("presents the remote title through the shared detail hierarchy", async () => {
    render(ManagedHoldingDetail, {
      connection,
      detail: snapshot,
      options: { profiles: [{ id: "4", label: "HD-1080p" }], roots: [] },
      checking: true,
    });

    expect(screen.getByRole("heading", { level: 1, name: "Arrival" })).toBeInTheDocument();
    expect(screen.getByText("Movie in Radarr")).toBeInTheDocument();
    expect(screen.getByText("Profile: HD-1080p")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Overview" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Files/ })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "In Prismedia" })).toBeInTheDocument();
    expect(screen.getAllByText("Checking availability").length).toBeGreaterThan(0);

    await fireEvent.click(screen.getByRole("tab", { name: "In Prismedia" }));
    expect(screen.getByText("When linked, Prismedia reads Radarr's files in place and follows file changes automatically.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Find this title in Prismedia" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Link to your Prismedia library" })).not.toBeInTheDocument();
    expect(screen.queryByText("Library link")).not.toBeInTheDocument();
  });

  it("does not describe an empty file list as locally available", () => {
    render(ManagedHoldingDetail, {
      connection,
      detail: { ...snapshot, item: { ...snapshot.item, remoteFileCount: 0 }, files: [] },
      localFiles: [],
    });

    expect(screen.getAllByText("No files reported").length).toBeGreaterThan(0);
    expect(screen.queryByText("Files readable by Prismedia")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /check local access/i })).not.toBeInTheDocument();
  });

  it("uses source artwork and metadata without creating a persisted entity", () => {
    const posterUrl = "https://images.example.test/poster.jpg";
    const backdropUrl = "https://images.example.test/backdrop.jpg";
    const { container } = render(ManagedHoldingDetail, {
      connection,
      detail: {
        ...snapshot,
        item: {
          ...snapshot.item,
          presentation: {
            overview: "A linguist works to understand an alien language.",
            posterUrl,
            backdropUrl,
            genres: ["Science Fiction", "Drama"],
            runtimeMinutes: 116,
            contentRating: "PG-13",
          },
        },
      },
    });

    expect(container.querySelector(`img[src="${posterUrl}"]`)).toBeInTheDocument();
    expect(container.querySelector(`img[src="${backdropUrl}"]`)).toBeInTheDocument();
    expect(screen.getByText("A linguist works to understand an alien language.")).toBeInTheDocument();
    expect(screen.getByText("Science Fiction")).toBeInTheDocument();
    expect(screen.getByText("Drama")).toBeInTheDocument();
    expect(screen.getAllByText("1h 56m").length).toBeGreaterThan(0);
    expect(screen.getAllByText("PG-13").length).toBeGreaterThan(0);
    const synopsis = screen.getByText("A linguist works to understand an alien language.");
    const availability = screen.getByRole("heading", { level: 3, name: "Availability" });
    expect(synopsis.compareDocumentPosition(availability) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("reports read access without claiming the title is in the Prismedia library", () => {
    render(ManagedHoldingDetail, {
      connection,
      detail: snapshot,
      localFiles: [{
        remoteId: "file-1",
        libraryRootId: "root",
        localPath: "/library/Arrival.mkv",
        isReadable: true,
        sizeMatches: true,
        problem: null,
      }],
    });
    expect(screen.getAllByText("Files readable by Prismedia").length).toBeGreaterThan(0);
    expect(screen.getByText("1/1 readable")).toBeInTheDocument();
    expect(screen.getByText(/does not mean the title is scanned into your Prismedia library or ready to play/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /check local access/i })).not.toBeInTheDocument();
  });

  it("offers the existing folder mapping flow when source paths are not mapped", async () => {
    const onRecheckAvailability = vi.fn();
    render(ManagedHoldingDetail, {
      connection,
      detail: snapshot,
      onRecheckAvailability,
      localFiles: [{
        remoteId: "file-1",
        libraryRootId: null,
        localPath: null,
        isReadable: false,
        sizeMatches: false,
        problem: "No local mapping covers this remote file.",
      }],
    });

    expect(screen.getAllByText("Library folder setup needed").length).toBeGreaterThan(0);
    await fireEvent.click(screen.getByRole("button", { name: "Set up library folder" }));
    expect(screen.getByRole("dialog", { name: "Library folder setup · Radarr" })).toBeInTheDocument();
    expect(screen.getByText(/files stay where they are and remain under Radarr's control/i)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Done" }));
    expect(onRecheckAvailability).toHaveBeenCalledOnce();
  });

  it("keeps title metadata visible when the availability check fails", () => {
    render(ManagedHoldingDetail, {
      connection,
      detail: snapshot,
      availabilityError: "Radarr did not respond.",
    });

    expect(screen.getByRole("heading", { level: 1, name: "Arrival" })).toBeInTheDocument();
    expect(screen.getAllByText("Availability unknown").length).toBeGreaterThan(0);
    expect(screen.getByText(/could not complete the file check.*Radarr did not respond/i)).toBeInTheDocument();
  });

  it("does not count unrelated or duplicate local evidence as verified files", () => {
    const secondFile = {
      ...snapshot.files[0]!,
      remoteId: "file-2",
      path: "/movies/Arrival/Arrival-featurette.mkv",
    };
    render(ManagedHoldingDetail, {
      connection,
      detail: { ...snapshot, files: [snapshot.files[0]!, secondFile] },
      localFiles: [
        { remoteId: "file-1", libraryRootId: "root", localPath: "/library/Arrival.mkv", isReadable: true, sizeMatches: true, problem: null },
        { remoteId: "file-1", libraryRootId: "root", localPath: "/library/Arrival-copy.mkv", isReadable: true, sizeMatches: true, problem: null },
        { remoteId: "unrelated", libraryRootId: "root", localPath: "/library/Other.mkv", isReadable: true, sizeMatches: true, problem: null },
      ],
    });

    expect(screen.getAllByText("1 of 2 files readable").length).toBeGreaterThan(0);
    expect(screen.queryByText("Files readable by Prismedia")).not.toBeInTheDocument();
  });

  it("preserves exact comic issue labels and offers reviewed library tracking", async () => {
    const comicDetail: ManagedItemSnapshot = {
      ...snapshot,
      item: { ...snapshot.item, entityKind: ENTITY_KIND.comicSeries, profileId: null },
      files: [{
        ...snapshot.files[0]!,
        targets: [
          { remoteId: "half", entityKind: ENTITY_KIND.comicInstallment, title: "Special", issueLabel: "½" },
          { remoteId: "decimal", entityKind: ENTITY_KIND.comicInstallment, title: "Interlude", issueLabel: "12.5" },
        ],
      }],
    };
    render(ManagedHoldingDetail, { connection, detail: comicDetail });

    expect(screen.getByRole("tab", { name: "In Prismedia" })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("tab", { name: /Files/ }));
    expect(screen.getByText("#½: Special")).toBeInTheDocument();
    expect(screen.getByText("#12.5: Interlude")).toBeInTheDocument();
  });

  it("reveals large file lists in bounded pages", async () => {
    const files = Array.from({ length: 51 }, (_, index) => ({
      ...snapshot.files[0]!,
      remoteId: `file-${index}`,
      path: `/movies/Arrival/file-${index}.mkv`,
    }));
    render(ManagedHoldingDetail, { connection, detail: { ...snapshot, files } });

    await fireEvent.click(screen.getByRole("tab", { name: /Files/ }));
    expect(screen.queryByText("/movies/Arrival/file-50.mkv")).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Show 1 more files" }));
    expect(screen.getByText("/movies/Arrival/file-50.mkv")).toBeInTheDocument();
  });
});
