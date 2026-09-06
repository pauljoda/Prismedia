import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import ConfirmDialog from "./ConfirmDialog.svelte";

describe("ConfirmDialog", () => {
  it("keeps failed async confirmation open and allows retry", async () => {
    const onClose = vi.fn();
    const onConfirm = vi.fn().mockRejectedValueOnce(new Error("Try again")).mockResolvedValueOnce(undefined);
    render(ConfirmDialog, { open: true, title: "Remove item?", message: "The item will be removed.", confirmLabel: "Remove", danger: true, onConfirm, onClose });
    expect(screen.getByRole("alertdialog", { name: "Remove item?" })).toBeTruthy();
    await fireEvent.click(screen.getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("Try again"));
    expect(onClose).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
  });

  it("focuses Cancel and blocks repeat actions and Escape while working", async () => {
    const onClose = vi.fn();
    let finish!: () => void;
    const onConfirm = vi.fn(() => new Promise<void>((resolve) => { finish = resolve; }));
    render(ConfirmDialog, { open: true, title: "Run action?", message: "Confirm this action.", onConfirm, onClose });
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Cancel" })));
    const confirm = screen.getByRole("button", { name: "Confirm" });
    await fireEvent.click(confirm);
    expect(confirm.hasAttribute("disabled")).toBe(true);
    expect(screen.getByRole("button", { name: "Cancel" }).hasAttribute("disabled")).toBe(true);
    await fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(onClose).not.toHaveBeenCalled();
    expect(onConfirm).toHaveBeenCalledOnce();
    finish();
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
  });
});
