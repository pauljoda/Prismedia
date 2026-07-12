import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import MediaProgressPanel from "./MediaProgressPanel.svelte";

describe("MediaProgressPanel listening mode", () => {
  it("renders independent listening labels and toggles listened state", async () => {
    const onToggleCompleted = vi.fn();
    render(MediaProgressPanel, {
      props: {
        kind: "listen",
        completed: false,
        percent: 25,
        positionLabel: "1:00:00 / 4:00:00",
        onToggleCompleted,
      },
    });

    expect(screen.getByText("Listening", { selector: ".kicker" })).toBeInTheDocument();
    expect(screen.getByText("Listening", { selector: ".status" })).toBeInTheDocument();
    expect(screen.getByText("25%")).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Mark listened" }));

    expect(onToggleCompleted).toHaveBeenCalledWith(true);
  });
});
