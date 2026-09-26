import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import type { BookAcquisitionProfileView } from "$lib/api/generated/model";
import Harness from "./RequestTargetOptions.test-harness.svelte";

const api = vi.hoisted(() => ({
  fetchAccessibleLibraryRoots: vi.fn(),
  fetchAcquisitionProfiles: vi.fn(),
}));

vi.mock("$lib/api/settings", () => ({ fetchAccessibleLibraryRoots: api.fetchAccessibleLibraryRoots }));
vi.mock("$lib/api/acquisitions", () => ({ fetchAcquisitionProfiles: api.fetchAcquisitionProfiles }));

describe("RequestTargetOptions", () => {
  beforeEach(() => {
    api.fetchAccessibleLibraryRoots.mockReset();
    api.fetchAcquisitionProfiles.mockReset();
  });

  afterEach(cleanup);

  it("replaces a read-only profile default and stale selection with a writable native destination", async () => {
    api.fetchAccessibleLibraryRoots.mockResolvedValue([
      root("mapped", "Mapped manager library", true),
      root("native", "Native books", false),
    ]);
    api.fetchAcquisitionProfiles.mockResolvedValue([profile("default-books", "mapped")]);

    render(Harness, { initialTargetLibraryRootId: "mapped" });

    await waitFor(() => expect(screen.getByTestId("profile")).toHaveTextContent("default-books"));
    expect(screen.getByTestId("target-root")).toHaveTextContent("native");
    const destination = screen.getByRole("button", { name: "Import destination" });
    expect(destination).toHaveTextContent("Native books");

    destination.focus();
    await fireEvent.keyDown(destination, { key: "ArrowDown" });
    await screen.findByRole("listbox");
    expect(screen.queryByRole("option", { name: "Mapped manager library" })).not.toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Native books" })).toBeInTheDocument();
  });
});

function root(id: string, label: string, isReadOnly: boolean) {
  return {
    id,
    label,
    scanVideos: false,
    scanImages: false,
    scanAudio: false,
    scanBooks: true,
    isNsfw: false,
    isReadOnly,
  };
}

function profile(id: string, targetLibraryRootId: string): BookAcquisitionProfileView {
  return {
    id,
    kind: ENTITY_KIND.book,
    displayName: "Default books",
    isDefault: true,
    targetLibraryRootId,
  } as BookAcquisitionProfileView;
}
