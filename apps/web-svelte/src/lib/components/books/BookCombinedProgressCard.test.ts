import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import BookCombinedProgressCard from "./BookCombinedProgressCard.svelte";

describe("BookCombinedProgressCard", () => {
  afterEach(cleanup);

  it("shows one shared progress track and all three resume choices", async () => {
    const onRead = vi.fn();
    const onListen = vi.fn();
    const onCombined = vi.fn();
    render(BookCombinedProgressCard, {
      progressPercent: 50,
      progressLabel: "50% of book",
      activityLabel: "3h 20m read or listened",
      onRead,
      onListen,
      onCombined,
    });

    expect(screen.getByText("50% of book")).toBeInTheDocument();
    expect(screen.getByText("3h 20m read or listened")).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Continue reading" }));
    await fireEvent.click(screen.getByRole("button", { name: "Continue listening" }));
    await fireEvent.click(screen.getByRole("button", { name: "Continue both" }));

    expect(onRead).toHaveBeenCalledOnce();
    expect(onListen).toHaveBeenCalledOnce();
    expect(onCombined).toHaveBeenCalledOnce();
  });
});
