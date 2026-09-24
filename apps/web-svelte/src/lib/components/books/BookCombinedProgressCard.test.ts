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

  it("explains an unaligned chapter instead of jumping to another one", async () => {
    const onCombined = vi.fn();
    render(BookCombinedProgressCard, {
      progressPercent: 5,
      listenLabel: "Continue listening ≈",
      listenHint: "Listening estimated from where you stopped reading.",
      combinedLabel: "Read & listen",
      combinedDisabled: true,
      explanation: "“Epigraphs” has no matching audiobook chapter.",
      onRead: vi.fn(),
      onListen: vi.fn(),
      onCombined,
    });

    expect(screen.getByText(/has no matching audiobook chapter/)).toBeInTheDocument();
    expect(screen.getByText(/estimated from where you stopped reading/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Read & listen" })).toBeDisabled();
    expect(onCombined).not.toHaveBeenCalled();
  });
});
