import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import BookCombinedProgressCard from "./BookCombinedProgressCard.svelte";

describe("BookCombinedProgressCard", () => {
  afterEach(cleanup);

  it("shows both progress tracks and all three continue choices", async () => {
    const onRead = vi.fn();
    const onListen = vi.fn();
    const onCombined = vi.fn();
    render(BookCombinedProgressCard, {
      readingPercent: 50,
      listeningPercent: 35,
      readingLabel: "50% of book",
      listeningLabel: "2:10:00 / 6:00:00",
      onRead,
      onListen,
      onCombined,
    });

    expect(screen.getByText("50% of book")).toBeInTheDocument();
    expect(screen.getByText("2:10:00 / 6:00:00")).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Continue reading" }));
    await fireEvent.click(screen.getByRole("button", { name: "Continue listening" }));
    await fireEvent.click(screen.getByRole("button", { name: "Continue reading and listening" }));

    expect(onRead).toHaveBeenCalledOnce();
    expect(onListen).toHaveBeenCalledOnce();
    expect(onCombined).toHaveBeenCalledOnce();
  });
});
