// Session store: the signed-in user and their permission flags, provided from the root
// layout after the boot-time /api/auth/me check. There is no loading state — the root
// load blocks rendering until the session resolves, so components never observe an
// unresolved session. Auth transitions (login, logout, setup) use full-page navigations
// so the whole app re-boots through the guard with clean state.

import { USER_ROLE } from "$lib/api/generated/codes";
import { logout as logoutApi, fetchCurrentUser, type AuthUser } from "$lib/api/auth";
import { createContext } from "$lib/utils/context";

const ctx = createContext<SessionStore>("Session");

export type SessionStatus = "authed" | "anonymous" | "needs-setup";

export class SessionStore {
  user = $state<AuthUser | null>(null);
  status = $state<SessionStatus>("anonymous");

  constructor(initial: { user: AuthUser | null; needsSetup: boolean }) {
    this.user = initial.user;
    this.status = initial.needsSetup ? "needs-setup" : initial.user ? "authed" : "anonymous";
  }

  get isAdmin(): boolean {
    return this.user?.role === USER_ROLE.admin;
  }

  get allowNsfw(): boolean {
    return this.user?.allowNsfw ?? false;
  }

  get canCreateLibraries(): boolean {
    return this.isAdmin || (this.user?.canCreateLibraries ?? false);
  }

  get canRequestContent(): boolean {
    return this.isAdmin || (this.user?.canRequestContent ?? false);
  }

  /** Gates the Settings surface: admins fully, members only for their own libraries. */
  get canManageServer(): boolean {
    return this.isAdmin || this.canCreateLibraries;
  }

  /** Re-reads the signed-in user after profile edits. */
  async refresh(): Promise<void> {
    const user = await fetchCurrentUser();
    if (user) {
      this.user = user;
      this.status = "authed";
    }
  }

  /** Signs out and re-boots the app on the login page. */
  async logout(): Promise<void> {
    try {
      await logoutApi();
    } catch {
      // Best effort: the cookie may already be dead; the reload lands on /login anyway.
    }

    window.location.replace("/login");
  }
}

export function provideSession(initial: { user: AuthUser | null; needsSetup: boolean }): SessionStore {
  return ctx.provide(new SessionStore(initial));
}

export const useSession = ctx.use;
