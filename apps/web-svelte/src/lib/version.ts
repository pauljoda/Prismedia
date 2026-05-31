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

export async function fetchReleaseUpdateStatus(
  fetchImpl: typeof fetch = fetch,
  options?: { force?: boolean },
): Promise<ReleaseUpdateStatus | null> {
  try {
    const res = await fetchImpl(`/api/update-check${options?.force ? "?force=1" : ""}`);
    if (!res.ok) return null;
    return (await res.json()) as ReleaseUpdateStatus;
  } catch {
    return null;
  }
}
