import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { Dialog } from "@prismedia/ui-svelte";
import { createRawSnippet } from "svelte";
import { afterEach, describe, expect, it, vi } from "vitest";

const children = createRawSnippet(() => ({ render: () => "<p>Dialog body</p>" }));

describe("Dialog", () => {
  afterEach(cleanup);

  it("mounts independent dialogs in opening order and returns focus to the lower dialog", async () => {
    const fields = createRawSnippet(() => ({ render: () => '<input aria-label="Editor field" />' }));
    const first = render(Dialog, { open: false, ariaLabel: "Global search", onClose: vi.fn(), children });
    render(Dialog, { open: true, ariaLabel: "Editor", onClose: vi.fn(), children: fields,
      initialFocus: () => screen.queryByRole("textbox", { name: "Editor field" }) });
    const field = screen.getByRole("textbox", { name: "Editor field" });
    await waitFor(() => expect(field).toHaveFocus());
    await first.rerender({ open: true });
    const editor = screen.getByRole("dialog", { name: "Editor" });
    const search = screen.getByRole("dialog", { name: "Global search" });
    expect(editor.compareDocumentPosition(search) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);
    await first.rerender({ open: false });
    await waitFor(() => expect(field).toHaveFocus());
    expect(editor).toBeInTheDocument();
  });

  it("opens modally and requests close for Escape or backdrop dismissal", async () => {
    const onClose = vi.fn();
    const view = render(Dialog, {
      open: true,
      ariaLabel: "Shared dialog",
      onClose,
      children,
    });

    const dialog = screen.getByRole("dialog", { name: "Shared dialog" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    await fireEvent.keyDown(dialog, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);

    onClose.mockClear();
    await view.rerender({
      open: false,
      ariaLabel: "Shared dialog",
      onClose,
      children,
    });
    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Shared dialog" })).not.toBeInTheDocument());
    expect(onClose).not.toHaveBeenCalled();
  });

  it("blocks user dismissal while its caller is busy", async () => {
    const onClose = vi.fn();
    render(Dialog, {
      open: true,
      ariaLabel: "Busy dialog",
      dismissible: false,
      onClose,
      children,
    });

    const dialog = screen.getByRole("dialog", { name: "Busy dialog" });
    await fireEvent.keyDown(dialog, { key: "Escape" });
    await fireEvent.pointerDown(document.body);

    expect(onClose).not.toHaveBeenCalled();
  });

  it("focuses the requested task field on opening without stealing later focus", async () => {
    const fields = createRawSnippet(() => ({ render: () => '<div><button type="button">Other action</button><input aria-label="Task field" /></div>' }));
    const props = {
      open: true, ariaLabel: "Focused dialog", onClose: vi.fn(), children: fields,
      initialFocus: () => screen.queryByRole("textbox", { name: "Task field" }),
    };
    const view = render(Dialog, props);
    await waitFor(() => expect(screen.getByRole("textbox", { name: "Task field" })).toHaveFocus());
    const action = screen.getByRole("button", { name: "Other action" });
    action.focus();
    await view.rerender({ ...props, class: "w-full" });
    expect(action).toHaveFocus();
  });
});
