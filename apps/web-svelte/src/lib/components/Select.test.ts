import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Select } from "@prismedia/ui-svelte";

afterEach(cleanup);

const options = [
  { value: "a", label: "Alpha" },
  { value: "b", label: "Bravo", disabled: true },
  { value: "c", label: "Charlie", annotation: "Mapped" },
];

describe("Select", () => {
  it("opens with an accessible name and skips disabled choices with the keyboard", async () => {
    const onchange = vi.fn();
    render(Select, { options, value: "a", ariaLabel: "Source file", onchange });
    const trigger = screen.getByRole("button", { name: "Source file" });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    await screen.findByRole("listbox");
    expect(screen.getByRole("option", { name: "Bravo" })).toHaveAttribute("aria-disabled", "true");
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    await fireEvent.keyDown(trigger, { key: "Enter" });
    expect(onchange).toHaveBeenCalledOnce();
    expect(onchange).toHaveBeenCalledWith("c");
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("keeps annotated options selectable and reports a selection once", async () => {
    const onchange = vi.fn();
    render(Select, { options, ariaLabel: "Source file", onchange });
    const trigger = screen.getByRole("button", { name: "Source file" });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    expect(await screen.findByText("Mapped")).toBeVisible();
    await fireEvent.pointerUp(screen.getByRole("option", { name: /Charlie/ }));
    expect(onchange).toHaveBeenCalledExactlyOnceWith("c");
  });

  it("does not clear the current value when it is selected again", async () => {
    const onchange = vi.fn();
    render(Select, { options, value: "a", ariaLabel: "Source file", onchange });
    const trigger = screen.getByRole("button", { name: "Source file" });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    await screen.findByRole("option", { name: "Alpha" });
    await fireEvent.keyDown(trigger, { key: "Enter" });
    expect(trigger).toHaveTextContent("Alpha");
    expect(onchange).not.toHaveBeenCalled();
  });

  it("closes on Escape without changing the value", async () => {
    const onchange = vi.fn();
    render(Select, { options, value: "a", ariaLabel: "Source file", onchange });
    const trigger = screen.getByRole("button", { name: "Source file" });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    await screen.findByRole("listbox");
    await fireEvent.keyDown(trigger, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("listbox")).not.toBeInTheDocument());
    expect(onchange).not.toHaveBeenCalled();
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("cannot open when disabled", async () => {
    render(Select, { options, ariaLabel: "Source file", disabled: true });
    const trigger = screen.getByRole("button", { name: "Source file" });
    expect(trigger).toBeDisabled();
    await fireEvent.click(trigger);
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("supports typeahead without opening the list", async () => {
    const onchange = vi.fn();
    render(Select, { options, value: "a", ariaLabel: "Source file", onchange });
    const trigger = screen.getByRole("button", { name: "Source file" });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "c" });
    expect(onchange).toHaveBeenCalledExactlyOnceWith("c");
    expect(trigger).toHaveTextContent("Charlie");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("reflects a value replaced by its parent", async () => {
    const { rerender } = render(Select, { options, value: "a", ariaLabel: "Source file" });
    await rerender({ value: "c" });
    expect(screen.getByRole("button", { name: "Source file" })).toHaveTextContent("Charlie");
  });

  it("allows an empty-valued option such as All sources", async () => {
    const onchange = vi.fn();
    render(Select, { options: [{ value: "", label: "All sources" }, ...options], value: "a", ariaLabel: "Source file", onchange });
    const trigger = screen.getByRole("button", { name: "Source file" });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "All sources" }));
    expect(onchange).toHaveBeenCalledExactlyOnceWith("");
    expect(trigger).toHaveTextContent("All sources");
  });
});
