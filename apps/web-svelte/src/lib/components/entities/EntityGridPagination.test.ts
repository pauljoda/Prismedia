import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityGridPagination from "./EntityGridPagination.svelte";

function props() {
  return {
    canPageBack: true, canPageForward: true, canSeekToEnd: true,
    currentPageIndex: 1, effectiveTotal: 400, loadingMore: false,
    normalizedPageSizeOptions: [100, 250, 500],
    onFirstPage: vi.fn(), onLastPage: vi.fn(), onNextPage: vi.fn(),
    onPreviousPage: vi.fn(), onPageSizeChange: vi.fn(), onLoadMore: vi.fn(),
    pageCount: 4, pageEnd: 200, pageSize: 100, pageStart: 100,
    pendingAdvanceAfterLoad: false, readoutPlaceholderWidth: 11, totalIsExact: true,
  };
}

describe("entity grid pagination controls", () => {
  it("uses a keyboard picker that cancels without changing the page size", async () => {
    const callbacks = props();
    render(EntityGridPagination, callbacks);
    const trigger = screen.getByRole("button", { name: "Per page" });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    expect(await screen.findByRole("option", { name: "100" })).toHaveAttribute("aria-selected", "true");
    await fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("listbox")).not.toBeInTheDocument());
    expect(trigger).toHaveFocus();
    expect(callbacks.onPageSizeChange).not.toHaveBeenCalled();

    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "250" }));
    expect(callbacks.onPageSizeChange).toHaveBeenCalledExactlyOnceWith(250);
  });

  it("keeps navigation callbacks and prevents advancing while a page is loading", async () => {
    const callbacks = props();
    const view = render(EntityGridPagination, callbacks);
    await fireEvent.click(screen.getByRole("button", { name: "First page" }));
    await fireEvent.click(screen.getByRole("button", { name: "Previous page" }));
    await fireEvent.click(screen.getByRole("button", { name: "Next page" }));
    await fireEvent.click(screen.getByRole("button", { name: "Last page" }));
    expect(callbacks.onFirstPage).toHaveBeenCalledOnce();
    expect(callbacks.onPreviousPage).toHaveBeenCalledOnce();
    expect(callbacks.onNextPage).toHaveBeenCalledOnce();
    expect(callbacks.onLastPage).toHaveBeenCalledOnce();

    await view.rerender({ ...callbacks, loadingMore: true });
    expect(screen.getByRole("button", { name: "Next page" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Last page" })).toBeDisabled();
    await view.rerender({ ...callbacks, loadMoreError: "Could not load this page" });
    expect(screen.getByRole("button", { name: "Next page" })).toBeDisabled();
    await fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(callbacks.onLoadMore).toHaveBeenCalledOnce();
  });
});
