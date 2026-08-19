import { describe, expect, it } from "vitest";
import { buildBrowserDeviceProfile, canPlayHevcMp4 } from "./browser-device-profile";

/** Builds a canPlayType stub that returns "probably" for MIME strings containing any allowed token. */
function canPlayTypeFor(allowedTokens: string[]): (mime: string) => string {
  return (mime: string) =>
    allowedTokens.some((token) => mime.includes(token)) ? "probably" : "";
}

describe("buildBrowserDeviceProfile", () => {
  it("advertises only the codecs the browser reports it can play", () => {
    // A baseline browser: H.264 + AAC in mp4, VP9 + Opus in webm. No HEVC/AV1.
    const profile = buildBrowserDeviceProfile(
      canPlayTypeFor(["avc1", "mp4a.40.2", "vp9", 'webm; codecs="opus"']),
    );

    const mp4 = profile.directPlayProfiles?.find((p) => p.container === "mp4");
    expect(mp4?.videoCodec).toBe("h264");
    expect(mp4?.audioCodec).toBe("aac");

    const webm = profile.directPlayProfiles?.find((p) => p.container === "webm");
    expect(webm?.videoCodec).toBe("vp9");
    expect(webm?.audioCodec).toBe("opus");
  });

  it("includes HEVC and AV1 when the browser supports them", () => {
    const profile = buildBrowserDeviceProfile(
      canPlayTypeFor(["avc1", "hvc1", "av01", "mp4a.40.2"]),
    );

    const mp4 = profile.directPlayProfiles?.find((p) => p.container === "mp4");
    expect(mp4?.videoCodec).toBe("h264,hevc,av1");
  });

  it("uses the detailed HEVC probe accepted by Chromium for both negotiation and playback", () => {
    const canPlayType = (mime: string) =>
      mime === 'video/mp4; codecs="hvc1.1.6.L93.B0"' ? "probably" : "";

    const profile = buildBrowserDeviceProfile(canPlayType);
    const mp4 = profile.directPlayProfiles?.find((entry) => entry.container === "mp4");

    expect(mp4?.videoCodec).toBe("hevc");
    expect(canPlayHevcMp4(canPlayType)).toBe(true);
  });

  it("never advertises MKV, so Matroska sources always transcode", () => {
    const profile = buildBrowserDeviceProfile(canPlayTypeFor(["avc1", "hvc1", "mp4a.40.2"]));
    const containers = profile.directPlayProfiles?.map((p) => p.container) ?? [];
    expect(containers).not.toContain("mkv");
    expect(containers).not.toContain("matroska");
  });

  it("omits a container entirely when no video codec is supported there", () => {
    // Only mp4/H.264 — no webm video codecs supported.
    const profile = buildBrowserDeviceProfile(canPlayTypeFor(["avc1", "mp4a.40.2"]));
    expect(profile.directPlayProfiles?.some((p) => p.container === "webm")).toBe(false);
    expect(profile.directPlayProfiles?.some((p) => p.container === "mp4")).toBe(true);
  });
});
