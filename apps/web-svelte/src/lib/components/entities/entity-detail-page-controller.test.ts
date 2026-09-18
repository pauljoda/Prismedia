import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { CAPABILITY_KIND, ENTITY_FILE_ROLE, MANAGED_REQUEST_PHASE } from "$lib/api/generated/codes";
import type { EntityCapability, ManagedRequestPhase } from "$lib/api/generated/model";
import EntityDetailPageControllerHarness, {
  type TestDetailEntity,
} from "./entity-detail-page-controller.test-harness.svelte";
import { entityDetailFreshnessFingerprint } from "./entity-detail-page-controller.svelte";

function entity(title: string): TestDetailEntity {
  return {
    id: "entity-1",
    kind: "test",
    title,
    capabilities: [],
  };
}

function externalEntity(
  phase: ManagedRequestPhase = MANAGED_REQUEST_PHASE.awaitingFiles,
  updatedAt = "2026-09-18T10:00:00Z",
): TestDetailEntity {
  return {
    ...entity("External title"),
    capabilities: [{
      kind: CAPABILITY_KIND.externalLibraryProvenance,
      connectionId: "connection",
      connectionName: "Radarr",
      pluginId: "radarr",
      libraryRootId: "library",
      libraryLabel: "Movies",
      request: { requestId: "request", phase, updatedAt, problem: null },
    }],
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((accept) => { resolve = accept; });
  return { promise, resolve };
}

describe("useEntityDetailPage", () => {
  it("loads an entity, publishes breadcrumbs, and refreshes when NSFW visibility changes", async () => {
    const load = vi
      .fn()
      .mockResolvedValueOnce(entity("Initial"))
      .mockResolvedValueOnce(entity("Refreshed"));

    render(EntityDetailPageControllerHarness, { props: { load } });

    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));
    expect(screen.getByTestId("entity-title")).toHaveTextContent("Initial");
    expect(screen.getByTestId("breadcrumbs")).toHaveTextContent("Entities / Initial");
    expect(load).toHaveBeenCalledTimes(1);
    expect(load.mock.calls[0][0].nsfwMode).toBe("off");

    await fireEvent.click(screen.getByRole("button", { name: "Show NSFW" }));

    await waitFor(() => expect(screen.getByTestId("entity-title")).toHaveTextContent("Refreshed"));
    expect(load).toHaveBeenCalledTimes(2);
    expect(load.mock.calls[1][0].nsfwMode).toBe("show");
  });

  it("surfaces load errors and retries through the same state machine", async () => {
    const load = vi
      .fn()
      .mockRejectedValueOnce(new Error("No connection"))
      .mockResolvedValueOnce(entity("Recovered"));

    render(EntityDetailPageControllerHarness, { props: { load } });

    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("error"));
    expect(screen.getByTestId("error-message")).toHaveTextContent("No connection");

    await fireEvent.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(screen.getByTestId("entity-title")).toHaveTextContent("Recovered"));
    expect(screen.getByTestId("load-state")).toHaveTextContent("ready");
  });

  it("centralizes optimistic rating and root metadata persistence", async () => {
    const load = vi.fn().mockResolvedValueOnce(entity("Initial")).mockResolvedValueOnce(entity("Saved"));
    const rating = vi.fn().mockResolvedValue(entity("Initial"));
    const metadata = vi.fn().mockResolvedValue(entity("Saved"));

    render(EntityDetailPageControllerHarness, {
      props: { load, mutations: { rating, metadata } },
    });
    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));

    await fireEvent.click(screen.getByRole("button", { name: "Rate" }));
    await waitFor(() => expect(rating).toHaveBeenCalledWith("entity-1", 4));

    await fireEvent.click(screen.getByRole("button", { name: "Save metadata" }));
    await waitFor(() => expect(screen.getByTestId("entity-title")).toHaveTextContent("Saved"));
    expect(metadata).toHaveBeenCalledWith(
      "entity-1",
      expect.objectContaining({ fields: ["title"] }),
    );
    expect(load).toHaveBeenCalledTimes(2);
  });

  it("fingerprints external state and source identity without timestamp or revision churn", () => {
    const first = externalEntity();
    first.hasSourceMedia = true;
    first.capabilities.push({
      kind: CAPABILITY_KIND.files,
      items: [{ role: ENTITY_FILE_ROLE.source, path: "/media/movie.mkv", mimeType: "video/x-matroska", fileId: "file-one", generation: 2 }],
      revision: 10,
    } as unknown as EntityCapability);
    const timestampOnly = structuredClone(first);
    const provenance = timestampOnly.capabilities[0] as unknown as Record<string, unknown>;
    (provenance.request as Record<string, unknown>).updatedAt = "2026-09-18T10:01:00Z";
    (timestampOnly.capabilities[1] as unknown as Record<string, unknown>).revision = 11;

    expect(entityDetailFreshnessFingerprint(timestampOnly)).toBe(entityDetailFreshnessFingerprint(first));

    const changedPath = structuredClone(first);
    ((changedPath.capabilities[1] as { items: Array<{ path: string }> }).items[0]).path = "/media/replaced.mkv";
    expect(entityDetailFreshnessFingerprint(changedPath)).not.toBe(entityDetailFreshnessFingerprint(first));

    const changedGeneration = structuredClone(first);
    ((changedGeneration.capabilities[1] as unknown as { items: Array<{ generation: number }> }).items[0]).generation = 3;
    expect(entityDetailFreshnessFingerprint(changedGeneration)).not.toBe(entityDetailFreshnessFingerprint(first));

    const archived = structuredClone(first);
    archived.capabilities.push({ kind: CAPABILITY_KIND.flags, isLibraryArchived: true } as unknown as EntityCapability);
    expect(entityDetailFreshnessFingerprint(archived)).not.toBe(entityDetailFreshnessFingerprint(first));
  });

  it("reloads route hydration on focus only when external state meaningfully changes", async () => {
    const initial = externalEntity();
    const load = vi.fn().mockResolvedValue(initial);
    const probe = vi.fn()
      .mockResolvedValueOnce(externalEntity(MANAGED_REQUEST_PHASE.awaitingFiles, "2026-09-18T10:05:00Z"))
      .mockResolvedValueOnce(externalEntity(MANAGED_REQUEST_PHASE.completed, "2026-09-18T10:06:00Z"));

    render(EntityDetailPageControllerHarness, { props: { load, freshnessProbe: probe } });
    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));

    window.dispatchEvent(new FocusEvent("focus"));
    await waitFor(() => expect(probe).toHaveBeenCalledTimes(1));
    expect(load).toHaveBeenCalledTimes(1);

    window.dispatchEvent(new FocusEvent("focus"));
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
  });

  it("keeps the hydrated route when a freshness probe fails", async () => {
    const load = vi.fn().mockResolvedValue(externalEntity());
    const probe = vi.fn().mockRejectedValue(new Error("Connection unavailable"));

    render(EntityDetailPageControllerHarness, { props: { load, freshnessProbe: probe } });
    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));
    window.dispatchEvent(new FocusEvent("focus"));
    await waitFor(() => expect(probe).toHaveBeenCalledTimes(1));

    expect(screen.getByTestId("entity-title")).toHaveTextContent("External title");
    expect(screen.getByTestId("load-state")).toHaveTextContent("ready");
    expect(screen.getByTestId("error-message").textContent).toBe("");
    expect(load).toHaveBeenCalledTimes(1);
  });

  it("ignores an older overlapping freshness response", async () => {
    const first = deferred<TestDetailEntity>();
    const second = deferred<TestDetailEntity>();
    const load = vi.fn().mockResolvedValue(externalEntity());
    const probe = vi.fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise);

    render(EntityDetailPageControllerHarness, { props: { load, freshnessProbe: probe } });
    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));
    window.dispatchEvent(new FocusEvent("focus"));
    await waitFor(() => expect(probe).toHaveBeenCalledTimes(1));
    window.dispatchEvent(new FocusEvent("focus"));
    await waitFor(() => expect(probe).toHaveBeenCalledTimes(2));

    second.resolve(externalEntity(MANAGED_REQUEST_PHASE.completed));
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
    first.resolve(externalEntity(MANAGED_REQUEST_PHASE.rejected));
    await Promise.resolve();
    expect(load).toHaveBeenCalledTimes(2);
  });

  it("ignores a probe captured before same-entity route hydration", async () => {
    const pendingProbe = deferred<TestDetailEntity>();
    const load = vi.fn()
      .mockResolvedValueOnce(externalEntity())
      .mockResolvedValueOnce({ ...externalEntity(), title: "Rehydrated title" });
    const probe = vi.fn().mockReturnValue(pendingProbe.promise);

    render(EntityDetailPageControllerHarness, { props: { load, freshnessProbe: probe } });
    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));
    window.dispatchEvent(new FocusEvent("focus"));
    await waitFor(() => expect(probe).toHaveBeenCalledTimes(1));

    await fireEvent.click(screen.getByRole("button", { name: "Show NSFW" }));
    await waitFor(() => expect(screen.getByTestId("entity-title")).toHaveTextContent("Rehydrated title"));
    pendingProbe.resolve(externalEntity(MANAGED_REQUEST_PHASE.completed));
    await Promise.resolve();

    expect(load).toHaveBeenCalledTimes(2);
  });

  it("aborts a hidden-page probe and checks again when the page becomes visible", async () => {
    let visibility: DocumentVisibilityState = "visible";
    const visibilitySpy = vi.spyOn(document, "visibilityState", "get").mockImplementation(() => visibility);
    const pendingProbe = deferred<TestDetailEntity>();
    let firstSignal: AbortSignal | undefined;
    const load = vi.fn().mockResolvedValue(externalEntity());
    const probe = vi.fn()
      .mockImplementationOnce((_entityId: string, context: { signal: AbortSignal }) => {
        firstSignal = context.signal;
        return pendingProbe.promise;
      })
      .mockResolvedValueOnce(externalEntity());
    try {
      render(EntityDetailPageControllerHarness, { props: { load, freshnessProbe: probe } });
      await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));
      window.dispatchEvent(new FocusEvent("focus"));
      await waitFor(() => expect(probe).toHaveBeenCalledTimes(1));

      visibility = "hidden";
      document.dispatchEvent(new Event("visibilitychange"));
      expect(firstSignal?.aborted).toBe(true);
      pendingProbe.resolve(externalEntity(MANAGED_REQUEST_PHASE.completed));

      visibility = "visible";
      document.dispatchEvent(new Event("visibilitychange"));
      await waitFor(() => expect(probe).toHaveBeenCalledTimes(2));
      expect(load).toHaveBeenCalledTimes(1);
    } finally {
      visibilitySpy.mockRestore();
    }
  });

  it("does not probe a hidden detail page when the window receives focus", async () => {
    const visibility = vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");
    try {
      const load = vi.fn().mockResolvedValue(externalEntity());
      const probe = vi.fn().mockResolvedValue(externalEntity(MANAGED_REQUEST_PHASE.completed));
      render(EntityDetailPageControllerHarness, { props: { load, freshnessProbe: probe } });
      await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));

      window.dispatchEvent(new FocusEvent("focus"));
      await Promise.resolve();

      expect(probe).not.toHaveBeenCalled();
      expect(load).toHaveBeenCalledTimes(1);
    } finally {
      visibility.mockRestore();
    }
  });
});
