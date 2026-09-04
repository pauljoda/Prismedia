import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { afterEach, beforeAll, expect, it } from "vitest";
import UiFoundationPreview from "./UiFoundationPreview.svelte";

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  HTMLDialogElement.prototype.close = function () { this.open = false; this.dispatchEvent(new Event("close")); };
});
afterEach(cleanup);

it("keeps the Select portal inside its native modal and returns focus on Escape", async () => {
  render(UiFoundationPreview);
  await fireEvent.click(screen.getByRole("button", { name: "Test inside a dialog" }));
  const dialog = screen.getByRole("dialog", { name: "Control preview" });
  const trigger = within(dialog).getByRole("button", { name: "Dialog source" });
  trigger.focus();
  await fireEvent.keyDown(trigger, { key: "ArrowDown" });
  const listbox = await screen.findByRole("listbox");
  expect(dialog).toContainElement(listbox);
  await fireEvent.keyDown(trigger, { key: "ArrowDown" });
  await fireEvent.keyDown(trigger, { key: "Enter" });
  expect(trigger).toHaveTextContent("Mapped source");
  expect(screen.getByRole("button", { name: "Preview source" })).toHaveTextContent("Mapped source");
  await fireEvent.keyDown(trigger, { key: "ArrowDown" });
  await fireEvent.keyDown(trigger, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("listbox")).not.toBeInTheDocument());
  expect(dialog).toHaveAttribute("open");
  expect(trigger).toHaveFocus();
});
