import { describe, expect, it, vi } from "vitest";
import packageJson from "../../package.json";
import { APP_VERSION, fetchReleaseUpdateStatus } from "./version";

describe("APP_VERSION", () => {
  it("matches the web package version", () => {
    expect(APP_VERSION).toBe(packageJson.version);
  });

  it("fetches release status without forcing by default", async () => {
    const fetchImpl = vi.fn(
      async () =>
        new Response(
          JSON.stringify({
            status: "current",
            localVersion: "0.22.1-dev",
            latestVersion: "0.22.1",
            latestUrl: "https://example.test",
            updateAvailable: false,
            checkedAt: "2026-05-11T12:00:00.000Z",
            fromCache: false,
          }),
        ),
    );

    const status = await fetchReleaseUpdateStatus(fetchImpl);

    expect(fetchImpl).toHaveBeenCalledWith("/api/update-check");
    expect(status?.status).toBe("current");
  });

  it("fetches release status with force for manual checks", async () => {
    const fetchImpl = vi.fn(
      async () =>
        new Response(
          JSON.stringify({
            status: "available",
            localVersion: "0.22.1-dev",
            latestVersion: "0.23.0",
            latestUrl: "https://example.test",
            updateAvailable: true,
            checkedAt: "2026-05-11T12:00:00.000Z",
            fromCache: false,
          }),
        ),
    );

    const status = await fetchReleaseUpdateStatus(fetchImpl, { force: true });

    expect(fetchImpl).toHaveBeenCalledWith("/api/update-check?force=1");
    expect(status?.updateAvailable).toBe(true);
  });
});
