import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import KeyValueEditor from "./KeyValueEditor.svelte";
import ListEditor from "./ListEditor.svelte";

afterEach(cleanup);

describe("shared metadata collection editors", () => {
  it("gives existing values distinct names and labels new entry fields", async () => {
    const onChange = vi.fn();
    render(KeyValueEditor, { values: [{ key: "provider-a", value: "123" }], label: "External IDs", keyLabel: "Provider", valueLabel: "ID", onChange });
    expect(screen.getByRole("textbox", { name: "provider-a ID" })).toHaveValue("123");
    await fireEvent.input(screen.getByLabelText("New Provider"), { target: { value: "provider-b" } });
    await fireEvent.input(screen.getByLabelText("New ID"), { target: { value: "456" } });
    await fireEvent.click(screen.getByRole("button", { name: "Add entry" }));
    expect(onChange).toHaveBeenCalledWith([{ key: "provider-a", value: "123" }, { key: "provider-b", value: "456" }]);
  });

  it("connects new entry errors to the invalid field", async () => {
    render(KeyValueEditor, { values: [], onChange: vi.fn(), validateValue: () => "Enter a number" });
    await fireEvent.input(screen.getByLabelText("New Key"), { target: { value: "count" } });
    const value = screen.getByLabelText("New Value");
    await fireEvent.input(value, { target: { value: "abc" } });
    await fireEvent.click(screen.getByRole("button", { name: "Add entry" }));
    expect(value).toHaveAttribute("aria-invalid", "true");
    expect(value).toHaveAccessibleDescription("Enter a number");
  });

  it("identifies the edit action and explains rejected link edits", async () => {
    const onChange = vi.fn();
    render(ListEditor, { values: ["https://example.com"], label: "Links", onChange, validate: value => value.startsWith("https://") ? null : "Use an HTTPS URL" });
    await fireEvent.click(screen.getByRole("button", { name: "Edit https://example.com" }));
    const input = screen.getByRole("textbox", { name: "Links item" });
    await fireEvent.input(input, { target: { value: "bad link" } });
    await fireEvent.keyDown(input, { key: "Enter" });
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAccessibleDescription("Use an HTTPS URL");
    expect(onChange).not.toHaveBeenCalled();
    await fireEvent.keyDown(input, { key: "Escape" });
    expect(screen.getByRole("button", { name: "Edit https://example.com" })).toBeInTheDocument();
  });
});
