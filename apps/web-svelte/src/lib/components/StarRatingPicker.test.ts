import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import StarRatingPicker from "./StarRatingPicker.svelte";

describe("shared rating control", () => {
  it("uses the 0-5 entity rating scale", async () => {
    const onChange = vi.fn();
    render(StarRatingPicker, {
      props: { value: 3, onChange, ariaLabelPrefix: "Rate track with" },
    });
    expect(screen.getByRole("button", { name: "Rate track with 3 star rating" })).toHaveAttribute(
      "aria-pressed", "true",
    );
    expect(screen.getByRole("button", { name: "Rate track with 3 star rating" })).toHaveClass("size-control");
    await fireEvent.click(screen.getByRole("button", { name: "Rate track with 4 star rating" }));
    expect(onChange).toHaveBeenCalledWith(4);
  });

  it("clears the rating when the selected star is clicked again", async () => {
    const onChange = vi.fn();
    render(StarRatingPicker, { props: { value: 4, onChange } });
    await fireEvent.click(screen.getByRole("button", { name: "Set 4 star rating" }));
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it("keeps the persisted value authoritative until a selection is accepted", async () => {
    const onChange = vi.fn();
    const { rerender } = render(StarRatingPicker, { value: null, onChange });
    const third = screen.getByRole("button", { name: "Set 3 star rating" });
    await fireEvent.click(third);
    expect(onChange).toHaveBeenCalledWith(3);
    expect(third).toHaveAttribute("aria-pressed", "false");
    await rerender({ value: 3, onChange });
    expect(third).toHaveAttribute("aria-pressed", "true");
    await fireEvent.click(third);
    expect(onChange).toHaveBeenLastCalledWith(null);
    expect(third).toHaveAttribute("aria-pressed", "true");
    await rerender({ value: null, onChange });
    expect(third).toHaveAttribute("aria-pressed", "false");
  });
});
