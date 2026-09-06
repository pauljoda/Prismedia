import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, expect, it, vi } from "vitest";
import { BOOK_FORMAT_TIER, BOOK_SOURCE_TIER, CUSTOM_FORMAT_CONDITION_TYPE, ENTITY_KIND, IMPORT_MODE } from "$lib/api/generated/codes";
import type { BookAcquisitionProfileSaveRequest, CustomFormatView } from "$lib/api/generated/model";
import { saveCustomFormat } from "$lib/api/acquisitions";
import AcquisitionProfileRules from "./AcquisitionProfileRules.svelte";

vi.mock("$lib/api/acquisitions", () => ({ saveCustomFormat: vi.fn() }));
afterEach(() => { cleanup(); vi.clearAllMocks(); });

it("persists edited conditions while keeping the score on the profile draft", async () => {
  const profile: BookAcquisitionProfileSaveRequest = {
    id: "profile", displayName: "Preferred audio", kind: ENTITY_KIND.movie, isDefault: false,
    targetLibraryRootId: "library", pathTemplate: "", importMode: IMPORT_MODE.copy,
    allowedFormats: [], preferredLanguages: [], minSeeders: 0, minSizeBytes: null, maxSizeBytes: null,
    requiredTerms: [], ignoredTerms: [], preferredTerms: [], weightedTerms: [],
    autoPick: false, autoRedownload: false, upgradeUntilCutoff: false,
    cutoffSourceTier: BOOK_SOURCE_TIER.unknown, cutoffFormatTier: BOOK_FORMAT_TIER.unknown,
    formatScores: { english: 100 }, minFormatScore: 0, cutoffFormatScore: null,
  };
  const rule: CustomFormatView = { id: "english", kind: ENTITY_KIND.movie, name: "English audio",
    conditions: [{ type: CUSTOM_FORMAT_CONDITION_TYPE.language, value: "English", required: true, negate: false }] };
  vi.mocked(saveCustomFormat).mockImplementation(async (request) => ({ ...request, id: "english" }));
  const onMessage = vi.fn();
  render(AcquisitionProfileRules, { profileForm: profile, customFormats: [rule], rulePresets: [], busy: false, onMessage, onError: vi.fn() });
  await fireEvent.click(screen.getByRole("button", { name: "Edit English audio" }));
  await fireEvent.input(screen.getByLabelText("Condition 1 value"), { target: { value: "ENG" } });
  await fireEvent.click(screen.getByLabelText("Exclude"));
  await fireEvent.input(screen.getByLabelText("Score in this profile"), { target: { value: "75" } });
  await fireEvent.click(screen.getByRole("radio", { name: "Basic" }));
  await fireEvent.click(screen.getByRole("radio", { name: "Advanced" }));
  expect(screen.getByLabelText("Exclude")).toBeChecked();
  await fireEvent.click(screen.getByRole("button", { name: "Save rule" }));
  await waitFor(() => expect(onMessage).toHaveBeenCalledWith("Rule saved. Save this profile to apply its score."));
  expect(saveCustomFormat).toHaveBeenCalledWith(expect.objectContaining({ conditions: [expect.objectContaining({ value: "ENG", negate: true })] }));
  expect(profile.formatScores).toEqual({ english: 75 });
  expect(rule.conditions[0].value).toBe("English");
  await fireEvent.click(screen.getByRole("button", { name: "Edit English audio" }));
  expect(screen.getByLabelText("Condition 1 value")).toHaveValue("ENG");
  expect(screen.getByLabelText("Score in this profile")).toHaveValue(75);
});
