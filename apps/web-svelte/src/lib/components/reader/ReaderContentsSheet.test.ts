import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import Harness from "./ReaderContentsSheet.test-harness.svelte";

describe("reader overlay composition", () => {
  it("keeps contents keys out of the reader and closes only the top layer", async () => {
    const onClose = vi.fn();
    const onNext = vi.fn();
    render(Harness, { onClose, onNext });
    const trigger = screen.getByRole("button", { name: "Open contents" });
    await waitFor(() => expect(screen.getByRole("button", { name: "Close" })).toHaveFocus());
    trigger.focus();
    await fireEvent.click(trigger);
    const closeContents = screen.getByRole("button", { name: "Close contents" });
    await waitFor(() => expect(closeContents).toHaveFocus());
    await fireEvent.keyDown(closeContents, { key: "ArrowRight" });
    expect(onNext).not.toHaveBeenCalled();
    await fireEvent.keyDown(closeContents, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Contents" })).not.toBeInTheDocument());
    expect(onClose).not.toHaveBeenCalled();
    await waitFor(() => expect(trigger).toHaveFocus());
    await fireEvent.keyDown(trigger, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
