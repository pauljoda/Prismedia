import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { afterEach, expect, it } from "vitest";
import UiFoundationPreview from "./UiFoundationPreview.svelte";

afterEach(cleanup);

it("keeps a portalled Select usable above a modal and returns focus on Escape", async () => {
  render(UiFoundationPreview);
  await fireEvent.click(screen.getByRole("button", { name: "Test inside a dialog" }));
  const dialog = screen.getByRole("dialog", { name: "Control preview" });
  // Let the modal's initial focus pass finish before exercising its child control.
  // JSDOM has no layout, so the focus scope falls back to the dialog itself.
  await waitFor(() => expect(dialog).toHaveFocus());
  const trigger = within(dialog).getByRole("button", { name: "Dialog source" });
  trigger.focus();
  await fireEvent.keyDown(trigger, { key: "ArrowDown" });
  const listbox = await screen.findByRole("listbox");
  expect(listbox).toBeVisible();
  expect(dialog).toHaveAttribute("aria-modal", "true");
  await fireEvent.keyDown(trigger, { key: "ArrowDown" });
  await fireEvent.keyDown(trigger, { key: "Enter" });
  expect(trigger).toHaveTextContent("Mapped source");
  expect(screen.getByRole("button", { name: "Preview source" })).toHaveTextContent("Mapped source");
  await fireEvent.keyDown(trigger, { key: "ArrowDown" });
  await fireEvent.keyDown(trigger, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("listbox")).not.toBeInTheDocument());
  expect(dialog).toHaveAttribute("data-state", "open");
  await waitFor(() => expect(trigger).toHaveFocus());
});
