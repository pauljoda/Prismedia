import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import TextField from "./TextField.svelte";
import TextAreaField from "./TextAreaField.svelte";

describe("shared form controls", () => {
  it("links text field validation to the named control", async () => {
    const onChange = vi.fn();
    render(TextField, { label: "Title", value: "", required: true, error: "Enter a title", onChange });
    const input = screen.getByRole("textbox", { name: "Title" });
    expect(input).toHaveAccessibleDescription("Enter a title");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toBeRequired();
    await fireEvent.input(input, { target: { value: "New title" } });
    expect(onChange).toHaveBeenCalledExactlyOnceWith("New title");
  });

  it("keeps textarea help and disabled state on the shared base", () => {
    render(TextAreaField, { label: "Summary", value: "Existing summary", helper: "A short description", disabled: true, onChange: vi.fn() });
    const input = screen.getByRole("textbox", { name: "Summary" });
    expect(input).toHaveAccessibleDescription("A short description");
    expect(input).toHaveAttribute("data-slot", "textarea");
    expect(input).toBeDisabled();
    expect(input).toHaveValue("Existing summary");
  });
});
