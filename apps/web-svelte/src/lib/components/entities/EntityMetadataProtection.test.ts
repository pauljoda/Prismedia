import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { METADATA_PATCH_FIELD, METADATA_VALUE_ORIGIN } from "$lib/api/generated/codes";
import EntityMetadataProtection from "./EntityMetadataProtection.svelte";

const api = vi.hoisted(() => ({ getEntityMetadataFields: vi.fn(), setEntityMetadataFieldLock: vi.fn() }));
vi.mock("$lib/api/generated/prismedia", () => api);
const field = {
  field: METADATA_PATCH_FIELD.description, origin: METADATA_VALUE_ORIGIN.user,
  providerId: null, observedAt: null, confidence: null, isCleared: true, isLocked: true, revision: 4,
};

describe("Metadata protection", () => {
  beforeEach(() => vi.resetAllMocks());

  it("identifies scan evidence and explains that locks also protect rescans", async () => {
    api.getEntityMetadataFields.mockResolvedValue({ status: 200, data: [{ ...field, origin: METADATA_VALUE_ORIGIN.scan, isCleared: false, isLocked: false }] });
    render(EntityMetadataProtection, { entityId: "entity-a" });
    await fireEvent.click(screen.getByRole("button", { name: "Metadata protection" }));
    expect(await screen.findByText("Library scan")).toBeInTheDocument();
    expect(screen.getByText(/metadata providers and library scans/)).toBeInTheDocument();
  });

  it("preserves attribution when unlocking a manual clear and sends its exact revision", async () => {
    api.getEntityMetadataFields.mockResolvedValue({ status: 200, data: [field] });
    api.setEntityMetadataFieldLock.mockResolvedValue({ status: 200, data: { ...field, isLocked: false, revision: 5 } });
    render(EntityMetadataProtection, { entityId: "entity-a" });
    await fireEvent.click(screen.getByRole("button", { name: "Metadata protection" }));
    await fireEvent.click(await screen.findByRole("button", { name: "Unlock Description" }));
    await screen.findByRole("button", { name: "Lock Description" });
    expect(api.setEntityMetadataFieldLock).toHaveBeenCalledWith("entity-a", field.field, { expectedRevision: 4, isLocked: false });
    expect(screen.getByText("Manual edit · Cleared")).toBeInTheDocument();
  });

  it("refreshes concurrent evidence without replaying a stale toggle", async () => {
    api.getEntityMetadataFields.mockResolvedValueOnce({ status: 200, data: [field] })
      .mockResolvedValueOnce({ status: 200, data: [{ ...field, revision: 6, isLocked: false, isCleared: false, origin: METADATA_VALUE_ORIGIN.provider, providerId: "book-provider" }] });
    api.setEntityMetadataFieldLock.mockResolvedValue({ status: 409, data: {} });
    render(EntityMetadataProtection, { entityId: "entity-a" });
    await fireEvent.click(screen.getByRole("button", { name: "Metadata protection" }));
    await fireEvent.click(await screen.findByRole("button", { name: "Unlock Description" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("This field changed"));
    expect(screen.getByText("book-provider")).toBeInTheDocument();
    expect(api.setEntityMetadataFieldLock).toHaveBeenCalledTimes(1);
  });
});
