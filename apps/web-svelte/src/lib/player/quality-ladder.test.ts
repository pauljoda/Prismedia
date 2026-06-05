import { describe, expect, it } from "vitest";
import { qualityRungsForSource } from "./quality-ladder";

describe("qualityRungsForSource", () => {
  it("offers every preset when the source bitrate is unknown (matches the server)", () => {
    const rungs = qualityRungsForSource(null, 2160, "hevc");
    expect(rungs).toHaveLength(14);
    expect(rungs[0]!.name).toBe("120mbps");
    expect(rungs.at(-1)!.name).toBe("420kbps");
  });

  it("caps each tier's height to the source resolution", () => {
    const rungs = qualityRungsForSource(null, 1080, "h264");
    // 2160-cap presets collapse to 1080 for a 1080p source.
    expect(rungs[0]).toMatchObject({ name: "120mbps", height: 1080, label: "1080p · 120 Mbps" });
    const lowest = rungs.at(-1)!;
    expect(lowest).toMatchObject({ name: "420kbps", height: 360, label: "360p · 420 kbps" });
  });

  it("keeps only presets at or below the source bitrate, plus the next tier up", () => {
    // A 10 Mbps H.264 1080p source: tiers <= 10 Mbps, plus the smallest preset above 10 Mbps (15 Mbps).
    const rungs = qualityRungsForSource(10_000_000, 1080, "h264");
    const names = rungs.map((r) => r.name);
    expect(names).toContain("15mbps");
    expect(names).not.toContain("20mbps");
    expect(names).toContain("10mbps");
    expect(names).toContain("420kbps");
    // Highest first.
    expect(rungs[0]!.bitrate).toBeGreaterThan(rungs[1]!.bitrate);
  });

  it("gives efficient codecs more headroom (HEVC counts as ~1.5x its bitrate)", () => {
    // 10 Mbps HEVC compares as 15 Mbps, so the 15 Mbps tier is included as an at-or-below tier and the
    // next tier up becomes 20 Mbps.
    const hevc = qualityRungsForSource(10_000_000, 2160, "hevc").map((r) => r.name);
    const h264 = qualityRungsForSource(10_000_000, 2160, "h264").map((r) => r.name);
    expect(hevc).toContain("20mbps");
    expect(h264).not.toContain("20mbps");
  });

  it("labels bitrates the way viewers expect", () => {
    const rungs = qualityRungsForSource(null, 2160, null);
    const labels = Object.fromEntries(rungs.map((r) => [r.name, r.label]));
    expect(labels["8mbps"]).toBe("1080p · 8 Mbps");
    expect(labels["1500kbps"]).toBe("720p · 1.5 Mbps");
    expect(labels["720kbps"]).toBe("480p · 720 kbps");
  });
});
