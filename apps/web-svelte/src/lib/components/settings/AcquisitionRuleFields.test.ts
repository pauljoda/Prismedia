import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, expect, it, vi } from "vitest";
import { CUSTOM_FORMAT_CONDITION_TYPE, ENTITY_KIND } from "$lib/api/generated/codes";
import type { CustomFormatSaveRequest } from "$lib/api/generated/model";
import AcquisitionRuleFields from "./AcquisitionRuleFields.svelte";

afterEach(cleanup);

it("keeps a Basic-generated rule editable through repeated Advanced switches", async () => {
  const form: CustomFormatSaveRequest = { id: null, kind: ENTITY_KIND.movie, name: "", conditions: [] };
  const onpreset = vi.fn();
  render(AcquisitionRuleFields, { form, presets: [{ name: "English audio", description: "Explicit audio", suggestedScore: 100,
    conditions: [{ type: CUSTOM_FORMAT_CONDITION_TYPE.language, value: "english", negate: false, required: true }] }], onpreset });
  await fireEvent.click(screen.getByRole("button", { name: /^Common preference:/ }));
  await fireEvent.click(screen.getByRole("option", { name: "English audio" }));
  await fireEvent.click(screen.getByRole("button", { name: "Use this preference" }));
  expect(onpreset).toHaveBeenCalledOnce();
  await fireEvent.click(screen.getByRole("radio", { name: "Advanced" }));
  await fireEvent.input(screen.getByLabelText("Condition 1 value"), { target: { value: "French" } });
  await fireEvent.click(screen.getByRole("radio", { name: "Basic" }));
  await fireEvent.click(screen.getByRole("radio", { name: "Advanced" }));
  expect(screen.getByLabelText("Condition 1 value")).toHaveValue("French");
  expect(screen.getByLabelText("Required")).toBeChecked();
});
