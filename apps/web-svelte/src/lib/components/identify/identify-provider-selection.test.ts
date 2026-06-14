import { describe, expect, it } from "vitest";
import { supportedProviderId } from "./identify-provider-selection";

describe("supportedProviderId", () => {
  const providers = [
    { id: "tmdb" },
    { id: "stash-box" },
  ];

  it("keeps an explicit selected provider when it is still supported", () => {
    expect(supportedProviderId(providers, "stash-box", "tmdb")).toBe("stash-box");
  });

  it("falls back from stale selected and queued providers to the first supported provider", () => {
    expect(supportedProviderId(providers, "anilist", "musicbrainz")).toBe("tmdb");
  });

  it("uses the queued provider only when the current kind supports it", () => {
    expect(supportedProviderId(providers, null, "stash-box")).toBe("stash-box");
  });
});
