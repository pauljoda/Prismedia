import { describe, expect, it } from "vitest";
import { buildBrowserDeviceProfile } from "./browser-device-profile";

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

    const mp4 = profile.DirectPlayProfiles?.find((p) => p.Container === "mp4");
    expect(mp4?.VideoCodec).toBe("h264");
    expect(mp4?.AudioCodec).toBe("aac");

    const webm = profile.DirectPlayProfiles?.find((p) => p.Container === "webm");
    expect(webm?.VideoCodec).toBe("vp9");
    expect(webm?.AudioCodec).toBe("opus");
  });

  it("includes HEVC and AV1 when the browser supports them", () => {
    const profile = buildBrowserDeviceProfile(
      canPlayTypeFor(["avc1", "hvc1", "av01", "mp4a.40.2"]),
    );

    const mp4 = profile.DirectPlayProfiles?.find((p) => p.Container === "mp4");
    expect(mp4?.VideoCodec).toBe("h264,hevc,av1");
  });

  it("never advertises MKV, so Matroska sources always transcode", () => {
    const profile = buildBrowserDeviceProfile(canPlayTypeFor(["avc1", "hvc1", "mp4a.40.2"]));
    const containers = profile.DirectPlayProfiles?.map((p) => p.Container) ?? [];
    expect(containers).not.toContain("mkv");
    expect(containers).not.toContain("matroska");
  });

  it("omits a container entirely when no video codec is supported there", () => {
    // Only mp4/H.264 — no webm video codecs supported.
    const profile = buildBrowserDeviceProfile(canPlayTypeFor(["avc1", "mp4a.40.2"]));
    expect(profile.DirectPlayProfiles?.some((p) => p.Container === "webm")).toBe(false);
    expect(profile.DirectPlayProfiles?.some((p) => p.Container === "mp4")).toBe(true);
  });
});
