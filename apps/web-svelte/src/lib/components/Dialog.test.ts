import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { Dialog } from "@prismedia/ui-svelte";
import { createRawSnippet } from "svelte";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";

const children = createRawSnippet(() => ({ render: () => "<p>Dialog body</p>" }));

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function showModal() {
    this.open = true;
  };
  HTMLDialogElement.prototype.close = function close() {
    this.open = false;
    this.dispatchEvent(new Event("close"));
  };
});

describe("Dialog", () => {
  afterEach(cleanup);

  it("opens modally and requests close for Escape or backdrop dismissal", async () => {
    const onClose = vi.fn();
    const view = render(Dialog, {
      open: true,
      ariaLabel: "Shared dialog",
      onClose,
      children,
    });

    const dialog = screen.getByRole("dialog", { name: "Shared dialog" }) as HTMLDialogElement;
    expect(dialog.open).toBe(true);

    await fireEvent(dialog, new Event("cancel", { cancelable: true }));
    await fireEvent.click(dialog);

    expect(onClose).toHaveBeenCalledTimes(2);

    onClose.mockClear();
    await view.rerender({
      open: false,
      ariaLabel: "Shared dialog",
      onClose,
      children,
    });
    expect(dialog.open).toBe(false);
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
    await fireEvent(dialog, new Event("cancel", { cancelable: true }));
    await fireEvent.click(dialog);

    expect(onClose).not.toHaveBeenCalled();
  });

  it("focuses the requested task field on opening without stealing later focus", async () => {
    const fields = createRawSnippet(() => ({ render: () => '<div><button type="button">Other action</button><input aria-label="Task field" /></div>' }));
    const props = {
      open: true, ariaLabel: "Focused dialog", onClose: vi.fn(), children: fields,
      initialFocus: () => screen.queryByRole("textbox", { name: "Task field" }),
    };
    const view = render(Dialog, props);
    expect(screen.getByRole("textbox", { name: "Task field" })).toHaveFocus();
    const action = screen.getByRole("button", { name: "Other action" });
    action.focus();
    await view.rerender({ ...props, class: "w-full" });
    expect(action).toHaveFocus();
  });
});
