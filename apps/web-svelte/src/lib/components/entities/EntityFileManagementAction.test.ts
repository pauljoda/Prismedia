import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { EntityCapability } from "$lib/api/generated/model";
import EntityFileManagementAction from "./EntityFileManagementAction.svelte";

const mocks = vi.hoisted(() => ({
  deleteMediaEntity: vi.fn(),
  canDeleteEntityFiles: vi.fn(),
}));

vi.mock("$lib/api/entity-deletion", () => ({
  deleteMediaEntity: mocks.deleteMediaEntity,
}));

vi.mock("$lib/api/capabilities", () => ({
  canDeleteEntityFiles: mocks.canDeleteEntityFiles,
}));

vi.mock("./ConfirmDialog.svelte", async () => ({
  default: (await import("../acquisitions/ConfirmDialog.test-stub.svelte")).default,
}));

describe("EntityFileManagementAction", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.canDeleteEntityFiles.mockReturnValue(true);
  });

  afterEach(cleanup);

  it("routes a monitored delete through the shared reverted callback", async () => {
    mocks.deleteMediaEntity.mockResolvedValue({
      deleted: 1,
      filesDeleted: 1,
      failures: [],
      reverted: 1,
    });
    const onDeleted = vi.fn();
    const onReverted = vi.fn(async () => {});

    render(EntityFileManagementAction, {
      entity: {
        id: "entity-1",
        title: "Season 1",
        capabilities: [] as EntityCapability[],
      },
      onDeleted,
      onReverted,
      compact: true,
    });

    await fireEvent.click(screen.getByRole("button", { name: /Delete files/ }));
    await fireEvent.click(screen.getByRole("button", { name: "Confirm Delete files" }));

    await waitFor(() => expect(onReverted).toHaveBeenCalledOnce());
    expect(onDeleted).not.toHaveBeenCalled();
  });

  it("routes a full removal through the shared deleted callback", async () => {
    mocks.deleteMediaEntity.mockResolvedValue({
      deleted: 1,
      filesDeleted: 1,
      failures: [],
      reverted: 0,
    });
    const onDeleted = vi.fn(async () => {});
    const onReverted = vi.fn();

    render(EntityFileManagementAction, {
      entity: {
        id: "entity-1",
        title: "Image",
        capabilities: [] as EntityCapability[],
      },
      onDeleted,
      onReverted,
    });

    await fireEvent.click(screen.getByRole("button", { name: /Delete files/ }));
    await fireEvent.click(screen.getByRole("button", { name: "Confirm Delete files" }));

    await waitFor(() => expect(onDeleted).toHaveBeenCalledOnce());
    expect(onReverted).not.toHaveBeenCalled();
  });

  it("keeps confirmation reconciliation active until the owning page has refreshed", async () => {
    mocks.deleteMediaEntity.mockResolvedValue({
      deleted: 1,
      filesDeleted: 1,
      failures: [],
      reverted: 1,
    });
    let finishRefresh: (() => void) | undefined;
    const onReverted = vi.fn(() => new Promise<void>((resolve) => {
      finishRefresh = resolve;
    }));

    render(EntityFileManagementAction, {
      entity: {
        id: "entity-1",
        title: "Season 1",
        capabilities: [] as EntityCapability[],
      },
      onDeleted: vi.fn(),
      onReverted,
      compact: true,
    });

    await fireEvent.click(screen.getByRole("button", { name: /Delete files/ }));
    await fireEvent.click(screen.getByRole("button", { name: "Confirm Delete files" }));
    await waitFor(() => expect(onReverted).toHaveBeenCalledOnce());

    expect(screen.getByRole("button", { name: "Confirm Delete files" })).toBeInTheDocument();
    finishRefresh?.();
  });

  it("stays hidden when the Entity has no source-backed file-management capability", () => {
    mocks.canDeleteEntityFiles.mockReturnValue(false);

    render(EntityFileManagementAction, {
      entity: {
        id: "wanted-1",
        title: "Wanted Movie",
        capabilities: [] as EntityCapability[],
      },
      onDeleted: vi.fn(),
      onReverted: vi.fn(),
    });

    expect(screen.queryByRole("button", { name: /Delete files/ })).not.toBeInTheDocument();
  });
});
