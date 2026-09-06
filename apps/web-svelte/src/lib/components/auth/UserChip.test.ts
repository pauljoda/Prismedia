import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import UserChip from "./UserChip.svelte";

const session = vi.hoisted(() => ({
  user: { displayName: "Local user", username: "local" },
  isAdmin: true,
  logout: vi.fn(),
}));
vi.mock("$lib/stores/session.svelte", () => ({ useSession: () => session }));

describe("UserChip", () => {
  beforeEach(() => {
    session.isAdmin = true;
    session.logout.mockClear();
    // JSDOM has no layout; give Floating UI a visible anchor and viewport.
    vi.spyOn(Element.prototype, "getBoundingClientRect").mockReturnValue(new DOMRect(100, 300, 240, 36));
    vi.spyOn(Element.prototype, "getClientRects").mockReturnValue([new DOMRect(100, 300, 240, 36)] as unknown as DOMRectList);
    vi.spyOn(document.documentElement, "clientWidth", "get").mockReturnValue(1024);
    vi.spyOn(document.documentElement, "clientHeight", "get").mockReturnValue(768);
  });
  afterEach(() => vi.restoreAllMocks());

  it("opens with the keyboard, supports typeahead, and returns focus on Escape", async () => {
    render(UserChip, { expanded: true });
    const trigger = screen.getByRole("button", { name: /Local user/ });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    const account = await screen.findByRole("menuitem", { name: "Account" });
    expect(screen.getByRole("menu")).toHaveAttribute("id", trigger.getAttribute("aria-controls"));
    await waitFor(() => expect(account).toHaveFocus());
    await fireEvent.keyDown(account, { key: "m" });
    const users = await screen.findByRole("menuitem", { name: "Manage users" });
    await waitFor(() => expect(users).toHaveFocus());
    expect(users).toHaveAttribute("href", "/settings/users");
    await fireEvent.keyDown(users, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("menu")).not.toBeInTheDocument());
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(session.logout).not.toHaveBeenCalled();
  });

  it("hides administration for members and signs out once on selection", async () => {
    session.isAdmin = false;
    render(UserChip, { expanded: false });
    await fireEvent.keyDown(screen.getByRole("button", { name: /Local user/ }), { key: "ArrowDown" });
    const signOut = await screen.findByRole("menuitem", { name: "Sign out" });
    expect(screen.queryByRole("menuitem", { name: "Manage users" })).not.toBeInTheDocument();
    await fireEvent.click(signOut);
    expect(session.logout).toHaveBeenCalledTimes(1);
  });
});
