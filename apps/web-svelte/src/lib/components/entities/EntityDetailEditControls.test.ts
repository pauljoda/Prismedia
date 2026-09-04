import { cleanup, render, screen } from "@testing-library/svelte";
import { afterEach, expect, it, vi } from "vitest";
import EntityDetailEditControls from "./EntityDetailEditControls.svelte";

afterEach(cleanup);

it("names the editing scope visibly and uses the shared error alert", () => {
  render(EntityDetailEditControls, { cancelLabel: "Cancel Metadata", saveLabel: "Save Metadata", errors: ["Enter a valid URL"], onCancel: vi.fn(), onSave: vi.fn(), saving: false, saveDisabled: false });
  expect(screen.getByRole("button", { name: "Save Metadata" })).toHaveTextContent("Save Metadata");
  expect(screen.getByRole("alert")).toHaveTextContent("Enter a valid URL");
});
