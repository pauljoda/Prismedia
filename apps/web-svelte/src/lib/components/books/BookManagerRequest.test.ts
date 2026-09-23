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
import type { CommitManagedBookRequestInput, ConnectionResponse, ManagedRequestResponse, ReviewManagedBookRequestInput, ReviewedManagedBookRendition } from "$lib/api/generated/model";
import BookManagerRequest from "./BookManagerRequest.svelte";

const api = vi.hoisted(() => ({
  fetchConnections: vi.fn(),
  fetchLibraryMounts: vi.fn(),
  reviewManagedBook: vi.fn(),
  saveManagedBook: vi.fn(),
}));
vi.mock("$lib/api/connections", () => ({ fetchConnections: api.fetchConnections }));
vi.mock("$lib/api/managed-libraries", () => ({ fetchLibraryMounts: api.fetchLibraryMounts }));
vi.mock("$lib/api/managed-requests", () => ({
  ManagedRequestRejectedError: class extends Error {},
}));
vi.mock("$lib/api/managed-book-requests", () => ({
  reviewManagedBook: api.reviewManagedBook,
  saveManagedBook: api.saveManagedBook,
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
function preview(rendition: typeof BOOK_RENDITION[keyof typeof BOOK_RENDITION]): ReviewedManagedBookRendition {
  return {
    rendition,
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
    api.reviewManagedBook.mockImplementation(async (_connectionId: string, input: ReviewManagedBookRequestInput) => ({
      connectionRevision: 1, title: "Frankenstein",
      renditions: input.renditions.map(choice => preview(choice.rendition)),
    }));
    api.saveManagedBook.mockImplementation(async (_connectionId: string, input: CommitManagedBookRequestInput) => ({
      entityId: "book", renditions: input.renditions.map(choice => ({
        rendition: choice.rendition, request: response(choice.rendition), error: null,
      })),
    }));
  });

  it("reviews and accepts both exact formats with one stable manager submission", async () => {
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
    await waitFor(() => expect(api.reviewManagedBook).toHaveBeenCalledTimes(1));
    await screen.findByRole("button", { name: "Request both formats" });
    expect(api.reviewManagedBook.mock.calls[0][1]).toEqual({
      entityId: "book", request: null, renditions: [
        { rendition: BOOK_RENDITION.ebook, libraryRootId: "ebook-root", search: false },
        { rendition: BOOK_RENDITION.audiobook, libraryRootId: "audio-root", search: false },
      ],
    });

    await fireEvent.click(screen.getByRole("button", { name: "Request both formats" }));
    await waitFor(() => expect(api.saveManagedBook).toHaveBeenCalledTimes(1));
    expect(api.saveManagedBook.mock.calls[0][1]).toEqual(expect.objectContaining({
      entityId: "book", request: null, expectedConnectionRevision: 1,
      renditions: [
        { rendition: BOOK_RENDITION.ebook, libraryRootId: "ebook-root", search: false },
        { rendition: BOOK_RENDITION.audiobook, libraryRootId: "audio-root", search: false },
      ],
    }));
    expect(onAccepted.mock.calls.map(call => call[0])).toEqual([BOOK_RENDITION.ebook, BOOK_RENDITION.audiobook]);
    expect(onChanged).toHaveBeenCalledOnce();
  });

  it("reviews the remaining format again after a partial failure", async () => {
    const onChanged = vi.fn();
    const onAccepted = vi.fn();
    api.saveManagedBook.mockResolvedValueOnce({ entityId: "book", renditions: [
      { rendition: BOOK_RENDITION.ebook, request: response(BOOK_RENDITION.ebook), error: null },
      { rendition: BOOK_RENDITION.audiobook, request: null, error: "Review and retry" },
    ] }).mockResolvedValueOnce({ entityId: "book", renditions: [
      { rendition: BOOK_RENDITION.ebook, request: response(BOOK_RENDITION.ebook), error: null },
      { rendition: BOOK_RENDITION.audiobook, request: response(BOOK_RENDITION.audiobook), error: null },
    ] });
    render(BookManagerRequest, { bookId: "book", title: "Frankenstein", hasEbook: false, hasAudiobook: false,
      acquisitions: [], monitors: [], managedRenditions: [], onAccepted, onChanged });
    await fireEvent.click(await screen.findByRole("checkbox", { name: "Ebook" }));
    await fireEvent.click(screen.getByRole("checkbox", { name: "Audiobook" }));
    await screen.findByRole("combobox", { name: "Ebook mapped library" });
    await chooseRoot("Ebook", "Ebooks");
    await chooseRoot("Audiobook", "Audiobooks");
    expect(screen.getByRole("button", { name: "Review manager request" })).toBeEnabled();
    await fireEvent.click(screen.getByRole("button", { name: "Review manager request" }));
    await waitFor(() => expect(api.reviewManagedBook).toHaveBeenCalledTimes(1));
    await fireEvent.click(await screen.findByRole("button", { name: "Request both formats" }));
    await screen.findByText(/Audiobook: Review and retry/);
    expect(onAccepted).toHaveBeenCalledExactlyOnceWith(BOOK_RENDITION.ebook);
    expect(onChanged).not.toHaveBeenCalled();
    expect(screen.getByRole("combobox", { name: "Ebook mapped library" })).toBeDisabled();
    expect(screen.getByRole("combobox", { name: "Audiobook mapped library" })).toBeEnabled();
    await fireEvent.click(screen.getByRole("button", { name: "Review manager request" }));
    await waitFor(() => expect(api.reviewManagedBook).toHaveBeenCalledTimes(2));
    await fireEvent.click(screen.getByRole("button", { name: "Request both formats" }));
    await waitFor(() => expect(api.saveManagedBook).toHaveBeenCalledTimes(2));
    expect(api.saveManagedBook.mock.calls[1][1].operationId)
      .not.toEqual(api.saveManagedBook.mock.calls[0][1].operationId);
    expect(onChanged).toHaveBeenCalledOnce();
    expect(onAccepted.mock.calls.map(call => call[0])).toEqual([BOOK_RENDITION.ebook, BOOK_RENDITION.audiobook]);
  });

  it("retries the same operation when acceptance cannot be confirmed", async () => {
    api.saveManagedBook.mockRejectedValueOnce(new Error("Could not confirm acceptance"));
    render(BookManagerRequest, { bookId: "book", title: "Frankenstein", hasEbook: false, hasAudiobook: false,
      acquisitions: [], monitors: [], managedRenditions: [] });
    await fireEvent.click(await screen.findByRole("checkbox", { name: "Ebook" }));
    await screen.findByRole("combobox", { name: "Ebook mapped library" });
    await chooseRoot("Ebook", "Ebooks");
    await fireEvent.click(screen.getByRole("button", { name: "Review manager request" }));
    await fireEvent.click(await screen.findByRole("button", { name: "Request ebook" }));
    await screen.findByText("Could not confirm acceptance");
    await fireEvent.click(screen.getByRole("button", { name: "Retry same request" }));
    await waitFor(() => expect(api.saveManagedBook).toHaveBeenCalledTimes(2));
    expect(api.saveManagedBook.mock.calls[1][1]).toEqual(api.saveManagedBook.mock.calls[0][1]);
  });

  it("submits fresh reviewed metadata and the selected manager format together", async () => {
    const onCompleted = vi.fn();
    const request = { kind: "book", pluginId: "open-library", bookRenditions: null } as never;
    render(BookManagerRequest, { request, title: "Frankenstein", hasEbook: false, hasAudiobook: false,
      acquisitions: [], monitors: [], managedRenditions: [], onCompleted });
    await fireEvent.click(await screen.findByRole("checkbox", { name: "Ebook" }));
    await screen.findByRole("combobox", { name: "Ebook mapped library" });
    await chooseRoot("Ebook", "Ebooks");
    await fireEvent.click(screen.getByRole("button", { name: "Review manager request" }));
    await waitFor(() => expect(api.reviewManagedBook).toHaveBeenCalledWith(connection.id, {
      entityId: null, request,
      renditions: [{ rendition: BOOK_RENDITION.ebook, libraryRootId: "ebook-root", search: false }],
    }));
    await fireEvent.click(await screen.findByRole("button", { name: "Request ebook" }));
    await waitFor(() => expect(api.saveManagedBook).toHaveBeenCalledWith(connection.id,
      expect.objectContaining({ entityId: null, request })));
    expect(onCompleted).toHaveBeenCalledExactlyOnceWith("book");
  });
});
