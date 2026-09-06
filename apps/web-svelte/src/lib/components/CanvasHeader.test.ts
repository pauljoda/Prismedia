import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import CanvasHeader from "./CanvasHeader.svelte";

vi.mock("$lib/nsfw/store.svelte", () => ({ useNsfw: () => ({ mode: "hide" }) }));

vi.mock("$lib/stores/app-chrome.svelte", () => ({
  useAppChrome: () => ({ breadcrumbs: [
    { label: "Settings", href: "/settings" },
    { label: "Acquisition", href: "/settings/acquisition" },
    { label: "Profiles", href: "/settings/acquisition/profiles" },
    { label: "Edit profile" },
  ] }),
}));
vi.mock("$lib/stores/session.svelte", () => ({ useSession: () => ({ canManageServer: false }) }));
vi.mock("$lib/stores/search.svelte", () => ({ useSearch: () => ({ openPalette: vi.fn() }) }));

describe("CanvasHeader", () => {
  it("opens an overflow menu outside the truncating nav and retains breadcrumb destinations", async () => {
    render(CanvasHeader);
    const trigger = screen.getAllByRole("button", { name: "More breadcrumbs" })[0];
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    const menu = await screen.findByRole("menu");
    expect(menu.closest("nav")).toBeNull();
    expect(screen.getByRole("menuitem", { name: "Profiles" })).toHaveAttribute("href", "/settings/acquisition/profiles");
    expect(menu).toHaveAttribute("id", trigger.getAttribute("aria-controls"));
    await fireEvent.keyDown(menu, { key: "Escape" });
    await waitFor(() => expect(trigger).toHaveFocus());
  });
});
