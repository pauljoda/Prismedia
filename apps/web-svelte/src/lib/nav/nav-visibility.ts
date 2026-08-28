import type { SessionStore } from "$lib/stores/session.svelte";

/** Nav hrefs restricted to admins (server-operating surfaces). */
const ADMIN_ONLY_HREFS = new Set(["/files", "/identify", "/downloads", "/plugins", "/jobs"]);

/**
 * Whether a nav item is visible to the current session. Settings shows for anyone who
 * can manage some part of the server (admins, members who may create libraries); the
 * operating surfaces are admin-only. Everything else (media browsing, search, stats)
 * is visible to every signed-in user.
 */
export function navItemVisible(href: string, session: SessionStore): boolean {
  if (href === "/request") {
    return session.canRequestContent;
  }

  if (href === "/settings") {
    return session.canManageServer;
  }

  if (ADMIN_ONLY_HREFS.has(href)) {
    return session.isAdmin;
  }

  return true;
}
