/**
 * The app version, read at build time from apps/web-svelte/package.json.
 */
import packageJson from "../../package.json";

export const APP_VERSION = packageJson.version;

export type ReleaseUpdateStatusKind = "available" | "current" | "unknown" | "development";

export interface ReleaseUpdateStatus {
  status: ReleaseUpdateStatusKind;
  /** Release channel of the running build: dev, alpha, beta, or release. */
  channel: string;
  localVersion: string;
  latestVersion: string | null;
  latestUrl: string | null;
  updateAvailable: boolean;
  checkedAt: string;
  fromCache: boolean;
  error?: string;
}

// Multiple nav surfaces (desktop sidebar, mobile more-sheet) ask for the update
// status on mount. The answer is identical for the whole session, so non-forced
// callers share one in-flight/settled promise instead of each hitting the API.
let sharedStatus: Promise<ReleaseUpdateStatus | null> | null = null;

async function requestReleaseUpdateStatus(
  fetchImpl: typeof fetch,
  force: boolean,
): Promise<ReleaseUpdateStatus | null> {
  try {
    const res = await fetchImpl(`/api/update-check${force ? "?force=1" : ""}`);
    if (!res.ok) return null;
    return (await res.json()) as ReleaseUpdateStatus;
  } catch {
    return null;
  }
}

export async function fetchReleaseUpdateStatus(
  fetchImpl: typeof fetch = fetch,
  options?: { force?: boolean },
): Promise<ReleaseUpdateStatus | null> {
  if (options?.force) {
    sharedStatus = requestReleaseUpdateStatus(fetchImpl, true);
    return sharedStatus;
  }
  sharedStatus ??= requestReleaseUpdateStatus(fetchImpl, false);
  return sharedStatus;
}

/** Clears the shared update-status cache so tests can exercise fresh fetches. */
export function resetReleaseUpdateStatusCache(): void {
  sharedStatus = null;
}
