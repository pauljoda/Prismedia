import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createAudioTabCoordinator, type AudioTabCoordinator } from "./audio-tab-coordinator";

describe("audio tab coordinator", () => {
  const coordinators: AudioTabCoordinator[] = [];

  beforeEach(() => {
    vi.stubGlobal("BroadcastChannel", undefined);
    window.localStorage.clear();
  });

  afterEach(() => {
    for (const coordinator of coordinators.splice(0)) {
      coordinator.destroy();
    }
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  function create() {
    const coordinator = createAudioTabCoordinator();
    coordinators.push(coordinator);
    return coordinator;
  }

  it("prevents passive restore from stealing an active tab lease", () => {
    const first = create();
    const second = create();

    expect(first.claimPlayback()).toBe(true);

    expect(second.claimPlayback({ steal: false })).toBe(false);
  });

  it("allows explicit playback to replace the active tab lease", () => {
    const first = create();
    const second = create();

    expect(first.claimPlayback()).toBe(true);

    expect(second.claimPlayback({ steal: true })).toBe(true);
    expect(first.claimPlayback({ steal: false })).toBe(false);
  });
});
