import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import ConfirmDeleteDialog from "./ConfirmDeleteDialog.svelte";

describe("ConfirmDeleteDialog", () => {
  it("offers separate library-only and disk-delete actions", async () => {
    const onDeleteFromLibrary = vi.fn();
    const onDeleteFromDisk = vi.fn();

    render(ConfirmDeleteDialog, {
      props: {
        open: true,
        entityType: "video",
        count: 2,
        onClose: vi.fn(),
        onDeleteFromLibrary,
        onDeleteFromDisk,
        allowDeleteFromDisk: true,
      },
    });

    expect(screen.getByText("Delete from Library")).toBeInTheDocument();
    expect(screen.getByText("Delete from Library and Disk")).toBeInTheDocument();

    await fireEvent.click(screen.getByText("Delete from Library"));
    await fireEvent.click(screen.getByText("Delete from Library and Disk"));

    expect(onDeleteFromLibrary).toHaveBeenCalledOnce();
    expect(onDeleteFromDisk).toHaveBeenCalledOnce();
  });
});
