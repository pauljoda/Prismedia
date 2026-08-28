import { describe, expect, it } from "vitest";
import { SessionStore } from "$lib/stores/session.svelte";
import { navItemVisible } from "./nav-visibility";

function member(canRequestContent: boolean): SessionStore {
  return new SessionStore({
    needsSetup: false,
    user: {
      id: "member-id",
      username: "member",
      displayName: "Member",
      role: "member",
      allowNsfw: false,
      canCreateLibraries: false,
      canRequestContent,
      enabled: true,
      lastLoginAt: null,
      createdAt: "2026-08-28T00:00:00Z",
      updatedAt: "2026-08-28T00:00:00Z",
    },
  });
}

describe("navItemVisible", () => {
  it("shows Request only to members with the request permission", () => {
    expect(navItemVisible("/request", member(true))).toBe(true);
    expect(navItemVisible("/request", member(false))).toBe(false);
  });
});
