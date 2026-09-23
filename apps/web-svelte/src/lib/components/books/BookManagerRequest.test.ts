import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  BOOK_RENDITION,
  CONNECTION_STATUS,
  ENTITY_KIND,
  INTEGRATION_OPERATION,
  MANAGED_REQUEST_PHASE,
  PLUGIN_CAPABILITY,
} from "$lib/api/generated/codes";
import type { ConnectionResponse, ManagedRequestPreview, ManagedRequestResponse } from "$lib/api/generated/model";
import BookManagerRequest from "./BookManagerRequest.svelte";

const api = vi.hoisted(() => ({
  fetchConnections: vi.fn(),
  fetchLibraryMounts: vi.fn(),
  fetchManagedRequestPreview: vi.fn(),
  saveManagedRequest: vi.fn(),
}));
vi.mock("$lib/api/connections", () => ({ fetchConnections: api.fetchConnections }));
vi.mock("$lib/api/managed-libraries", () => ({ fetchLibraryMounts: api.fetchLibraryMounts }));
vi.mock("$lib/api/managed-requests", () => ({
  fetchManagedRequestPreview: api.fetchManagedRequestPreview,
  saveManagedRequest: api.saveManagedRequest,
  ManagedRequestRejectedError: class extends Error {},
}));
vi.mock("@prismedia/ui-svelte", async (original) => ({
  ...await original<typeof import("@prismedia/ui-svelte")>(),
  Select: (await import("./BookManagerSelect.test-stub.svelte")).default,
}));

const connection: ConnectionResponse = {
  id: "lazylibrarian", pluginId: "lazylibrarian", name: "Book manager", baseUrl: "http://fixture.test",
  enabled: true, enabledCapabilities: [PLUGIN_CAPABILITY.connectedLibrary, PLUGIN_CAPABILITY.externalManager],
  settings: {}, configuredSecretKeys: [], revision: 1, status: CONNECTION_STATUS.ready,
  remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
  effectiveCapabilities: [
    { kind: PLUGIN_CAPABILITY.externalManager, entityKinds: [ENTITY_KIND.book], operations: [
      INTEGRATION_OPERATION.lookupManaged, INTEGRATION_OPERATION.reconcileManaged,
      INTEGRATION_OPERATION.configureManaged, INTEGRATION_OPERATION.requestManaged,
    ] },
    { kind: PLUGIN_CAPABILITY.connectedLibrary, entityKinds: [ENTITY_KIND.book], operations: [
      INTEGRATION_OPERATION.getLibraryItem, INTEGRATION_OPERATION.listLibraries,
    ] },
  ],
};
const ebookMount = { id: "ebook-mount", connectionId: connection.id, libraryRootId: "ebook-root", remoteRootId: "books",
  remotePath: "/books", localPath: "/mapped/books", label: "Ebooks" };
const audioMount = { id: "audio-mount", connectionId: connection.id, libraryRootId: "audio-root", remoteRootId: "audio",
  remotePath: "/audio", localPath: "/mapped/audio", label: "Audiobooks" };
function preview(rendition: typeof BOOK_RENDITION[keyof typeof BOOK_RENDITION]): ManagedRequestPreview {
  return {
    entityId: "book", title: "Frankenstein",
    work: { entityKind: ENTITY_KIND.book, externalIds: { openlibrarywork: "OL450063W" }, bookRendition: rendition },
    mount: rendition === BOOK_RENDITION.ebook ? ebookMount : audioMount,
    options: { profiles: [], roots: [] }, existing: null,
  };
}
function response(rendition: typeof BOOK_RENDITION[keyof typeof BOOK_RENDITION]): ManagedRequestResponse {
  return {
    id: `${rendition}-request`, connectionId: connection.id, entityId: "book",
    libraryRootId: rendition === BOOK_RENDITION.ebook ? ebookMount.libraryRootId : audioMount.libraryRootId,
    title: "Frankenstein", phase: MANAGED_REQUEST_PHASE.awaitingFiles, revision: 1, remoteId: "OL450063W",
    monitored: false, search: false, reviewRequired: false, canCancel: false,
    createdAt: "2026-09-23T12:00:00Z", updatedAt: "2026-09-23T12:00:00Z", problem: null,
    holdingId: `${rendition}-holding`,
  };
}

async function chooseRoot(rendition: "Ebook" | "Audiobook", root: "Ebooks" | "Audiobooks") {
  const select = screen.getByRole("combobox", { name: `${rendition} mapped library` });
  await fireEvent.change(select, { target: { value: root === "Ebooks" ? "ebook-root" : "audio-root" } });
  expect(select).toHaveValue(root === "Ebooks" ? "ebook-root" : "audio-root");
}

describe("Book manager request", () => {
  afterEach(cleanup);
  beforeEach(() => {
    vi.resetAllMocks();
    api.fetchConnections.mockResolvedValue([connection]);
    api.fetchLibraryMounts.mockResolvedValue([ebookMount, audioMount]);
    api.fetchManagedRequestPreview.mockImplementation(async (_connectionId, input) => preview(input.bookRendition));
    api.saveManagedRequest.mockImplementation(async (_connectionId, input) => response(input.reviewedWork.bookRendition));
  });

  it("previews and accepts ebook and audiobook as separate exact manager intents", async () => {
    const onChanged = vi.fn();
    const onAccepted = vi.fn();
    render(BookManagerRequest, { bookId: "book", title: "Frankenstein", hasEbook: false, hasAudiobook: false,
      acquisitions: [], monitors: [], managedRenditions: [], onAccepted, onChanged });
    await fireEvent.click(await screen.findByRole("checkbox", { name: "Ebook" }));
    await fireEvent.click(screen.getByRole("checkbox", { name: "Audiobook" }));
    await screen.findByRole("combobox", { name: "Ebook mapped library" });
    await chooseRoot("Ebook", "Ebooks");
    await chooseRoot("Audiobook", "Audiobooks");
    expect(screen.getByRole("combobox", { name: "Ebook mapped library" })).toHaveValue("ebook-root");
    expect(screen.getByRole("combobox", { name: "Audiobook mapped library" })).toHaveValue("audio-root");
    expect(screen.getByRole("button", { name: "Review manager request" })).toBeEnabled();
    await fireEvent.click(screen.getByRole("button", { name: "Review manager request" }));
    await waitFor(() => expect(api.fetchManagedRequestPreview).toHaveBeenCalledTimes(2));
    await screen.findByRole("button", { name: "Request both formats" });
    expect(api.fetchManagedRequestPreview.mock.calls.map(call => call[1])).toEqual([
      { entityId: "book", libraryRootId: "ebook-root", bookRendition: BOOK_RENDITION.ebook },
      { entityId: "book", libraryRootId: "audio-root", bookRendition: BOOK_RENDITION.audiobook },
    ]);

    await fireEvent.click(screen.getByRole("button", { name: "Request both formats" }));
    await waitFor(() => expect(api.saveManagedRequest).toHaveBeenCalledTimes(2));
    const intents = api.saveManagedRequest.mock.calls.map(call => call[1]);
    expect(intents.map(intent => [intent.libraryRootId, intent.reviewedWork.bookRendition, intent.profileId, intent.monitored])).toEqual([
      ["ebook-root", BOOK_RENDITION.ebook, null, true],
      ["audio-root", BOOK_RENDITION.audiobook, null, true],
    ]);
    expect(intents[0].operationId).not.toBe(intents[1].operationId);
    expect(onAccepted.mock.calls.map(call => call[0])).toEqual([BOOK_RENDITION.ebook, BOOK_RENDITION.audiobook]);
    expect(onChanged).toHaveBeenCalledOnce();
  });

  it("retries the same audiobook operation after an uncertain response without repeating the accepted ebook", async () => {
    const onChanged = vi.fn();
    const onAccepted = vi.fn();
    api.saveManagedRequest.mockResolvedValueOnce(response(BOOK_RENDITION.ebook))
      .mockRejectedValueOnce(new Error("Response interrupted"))
      .mockResolvedValueOnce(response(BOOK_RENDITION.audiobook));
    render(BookManagerRequest, { bookId: "book", title: "Frankenstein", hasEbook: false, hasAudiobook: false,
      acquisitions: [], monitors: [], managedRenditions: [], onAccepted, onChanged });
    await fireEvent.click(await screen.findByRole("checkbox", { name: "Ebook" }));
    await fireEvent.click(screen.getByRole("checkbox", { name: "Audiobook" }));
    await screen.findByRole("combobox", { name: "Ebook mapped library" });
    await chooseRoot("Ebook", "Ebooks");
    await chooseRoot("Audiobook", "Audiobooks");
    expect(screen.getByRole("button", { name: "Review manager request" })).toBeEnabled();
    await fireEvent.click(screen.getByRole("button", { name: "Review manager request" }));
    await waitFor(() => expect(api.fetchManagedRequestPreview).toHaveBeenCalledTimes(2));
    await fireEvent.click(await screen.findByRole("button", { name: "Request both formats" }));
    await screen.findByText(/Audiobook: Response interrupted/);
    expect(onAccepted).toHaveBeenCalledExactlyOnceWith(BOOK_RENDITION.ebook);
    expect(onChanged).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Retry same request" }));
    await waitFor(() => expect(api.saveManagedRequest).toHaveBeenCalledTimes(3));
    expect(api.saveManagedRequest.mock.calls[2][1]).toEqual(api.saveManagedRequest.mock.calls[1][1]);
    expect(api.saveManagedRequest.mock.calls[0][1].operationId).not.toBe(api.saveManagedRequest.mock.calls[2][1].operationId);
    expect(onChanged).toHaveBeenCalledOnce();
    expect(onAccepted.mock.calls.map(call => call[0])).toEqual([BOOK_RENDITION.ebook, BOOK_RENDITION.audiobook]);
  });
});
