import { redirect } from "@sveltejs/kit";
import { browser } from "$app/environment";
import type { LayoutLoad } from "./$types";
import { fetchCurrentUser, fetchSetupStatusWithRetry, type AuthUser } from "$lib/api/auth";
import { fetchSettingsValues } from "$lib/api/settings";
import { settingKeys, valuesToLibrarySettings } from "$lib/settings/app-settings";
import { readSidebarCookie } from "$lib/stores/app-chrome.svelte";
import type { NsfwMode } from "$lib/nsfw/cookie";

export const ssr = false;

/** Routes reachable without a session (rendered without the app shell). */
function isPublicPath(pathname: string): boolean {
  return pathname === "/login" || pathname === "/setup" || pathname.startsWith("/setup/");
}

/** Only same-origin absolute paths survive as post-login destinations. */
function safeReturnTo(url: URL): string {
  const value = url.searchParams.get("returnTo");
  return value && value.startsWith("/") && !value.startsWith("//") ? value : "/";
}

export const load: LayoutLoad = async ({ url, untrack }) => {
  // Untracked: the guard must not re-run (and re-fetch auth) on every client navigation;
  // beforeNavigate in the layout covers client-side transitions from in-memory state.
  const pathname = untrack(() => url.pathname);
  const search = untrack(() => url.search);
  const initialCollapsed = readSidebarCookie();

  // The two probes are independent: a failed "me" must not poison setup detection and
  // vice versa. Setup status retries briefly — a fresh install misread as "setup done"
  // because of one transient failure would strand the user on the login page.
  const [setup, userResult] = await Promise.all([
    fetchSetupStatusWithRetry(),
    fetchCurrentUser().catch(() => null),
  ]);
  const needsSetup = setup?.needsSetup ?? false;
  const user: AuthUser | null = userResult;

  // Setup outranks everything: an install without an admin only shows the wizard.
  // When setup state is UNKNOWN (setup === null, API unreachable) we fall through to the
  // login route rather than guessing — the login page re-checks setup status on mount
  // and forwards to the wizard as soon as the server answers.
  if (needsSetup && !pathname.startsWith("/setup")) {
    redirect(307, "/setup");
  }

  if (setup !== null && !needsSetup && pathname.startsWith("/setup")) {
    redirect(307, "/");
  }

  if (!user && !isPublicPath(pathname)) {
    redirect(307, `/login?returnTo=${encodeURIComponent(pathname + search)}`);
  }

  if (user && pathname === "/login") {
    redirect(307, untrack(() => safeReturnTo(url)));
  }

  // The NSFW store prefers the device cookie; the server default only matters on a
  // device's first visit. Skipping the fetch when the cookie exists removes a blocking
  // round trip from every returning session's cold load.
  const hasNsfwCookie =
    browser && document.cookie.match(/(?:^|;\s*)prismedia-nsfw-mode=([^;]*)/) !== null;
  let initialNsfwMode: NsfwMode | undefined;
  if (user && !hasNsfwCookie) {
    try {
      initialNsfwMode = valuesToLibrarySettings(
        (await fetchSettingsValues([settingKeys.visibilityDefaultMode])).values,
      ).visibilityDefaultMode;
    } catch {
      // Non-fatal: the NSFW store falls back to its off default.
    }
  }

  return {
    initialCollapsed,
    user,
    needsSetup,
    ...(initialNsfwMode !== undefined ? { initialNsfwMode } : {}),
  };
};
