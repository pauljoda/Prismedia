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

  it("shows a Separate book's reading and listening as two meters without switching", async () => {
    const onRead = vi.fn();
    const onListen = vi.fn();
    render(BookCombinedProgressCard, {
      separate: {
        readingPercent: 25,
        listeningPercent: 36,
        reason: "This audiobook has no chapter markers, so reading and listening are tracked separately.",
      },
      progressPercent: 80,
      progressLabel: "80% of book",
      switchLabel: "Read from your listening spot",
      onRead,
      onListen,
      onCombined: vi.fn(),
      onSwitch: vi.fn(),
    });

    expect(screen.getByText(/no chapter markers, so reading and listening are tracked separately/)).toBeInTheDocument();
    expect(screen.getByText("Reading")).toBeInTheDocument();
    expect(screen.getByText("25%")).toBeInTheDocument();
    expect(screen.getByText("Listening")).toBeInTheDocument();
    expect(screen.getByText("36%")).toBeInTheDocument();
    // The shared single progress and every switching action are gone.
    expect(screen.queryByText("80% of book")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Continue both" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Read from your listening spot" })).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Continue reading" }));
    await fireEvent.click(screen.getByRole("button", { name: "Continue listening" }));
    expect(onRead).toHaveBeenCalledOnce();
    expect(onListen).toHaveBeenCalledOnce();
  });
});
