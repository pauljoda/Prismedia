import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ACQUISITION_STATUS, ENTITY_KIND } from "$lib/api/generated/codes";
import type { AcquisitionDetail } from "$lib/api/generated/model";
import Harness from "./EntityAcquisitionCard.test-harness.svelte";

const mocks = vi.hoisted(() => ({
  deleteMediaEntity: vi.fn(),
}));

vi.mock("$lib/api/entity-deletion", () => ({
  deleteMediaEntity: mocks.deleteMediaEntity,
  isDeletableMediaKind: () => true,
}));

vi.mock("$lib/components/acquisitions/AcquisitionPanel.svelte", async () => ({
  default: (await import("./AcquisitionPanel.test-stub.svelte")).default,
}));

vi.mock("$lib/components/entities/ConfirmDialog.svelte", async () => ({
  default: (await import("./ConfirmDialog.test-stub.svelte")).default,
}));

describe("EntityAcquisitionCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.deleteMediaEntity.mockResolvedValue({
      deleted: 1,
      filesDeleted: 1,
      failures: [],
      reverted: 1,
    });
  });

  afterEach(cleanup);

  it("clears a reverted entity's stale acquisition before waiting for its targeted refresh", async () => {
    let finishRefresh!: () => void;
    const refresh = vi.fn(() => new Promise<void>((resolve) => (finishRefresh = resolve)));
    const onReverted = vi.fn(async () => {});
    const onDeleted = vi.fn();

    render(Harness, {
      initialAcquisition: acquisition("acquisition-1"),
      refresh,
      onReverted,
      onDeleted,
    });

    expect(screen.getByTestId("acquisition-panel")).toHaveTextContent("acquisition-1");
    await fireEvent.click(screen.getByRole("button", { name: "Delete files" }));
    await fireEvent.click(screen.getByRole("button", { name: "Confirm Delete files" }));

    await waitFor(() => expect(refresh).toHaveBeenCalledOnce());
    expect(screen.getByTestId("bound-acquisition-id")).toHaveTextContent("none");
    expect(screen.queryByTestId("acquisition-panel")).not.toBeInTheDocument();
    expect(onReverted).not.toHaveBeenCalled();
    expect(onDeleted).not.toHaveBeenCalled();

    finishRefresh();
    await waitFor(() => expect(onReverted).toHaveBeenCalledOnce());
    expect(onDeleted).not.toHaveBeenCalled();
  });
});

function acquisition(id: string): AcquisitionDetail {
  return {
    summary: {
      id,
      status: ACQUISITION_STATUS.imported,
      statusMessage: null,
      title: "Season 1",
      author: null,
      series: "Bluey",
      year: 2018,
      posterUrl: null,
      progress: 1,
      createdAt: "2026-07-09T00:00:00Z",
      updatedAt: "2026-07-09T00:00:00Z",
      kind: ENTITY_KIND.videoSeason,
      entityId: "season-1",
    },
    candidates: [],
  };
}
