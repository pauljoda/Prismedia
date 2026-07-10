import { fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import AudioWaveformFilmstrip from "./AudioWaveformFilmstrip.svelte";

vi.mock("@lucide/svelte", () => ({
  ChevronLeft: vi.fn(),
  ChevronRight: vi.fn(),
}));

describe("AudioWaveformFilmstrip", () => {
  beforeEach(() => {
    vi.stubGlobal("requestAnimationFrame", vi.fn(() => 1));
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    Object.defineProperty(window, "matchMedia", {
      configurable: true,
      value: vi.fn().mockReturnValue({
        matches: true,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }),
    });
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({
      width: 600,
      height: 52,
      x: 0,
      y: 0,
      top: 0,
      left: 0,
      right: 600,
      bottom: 52,
      toJSON: () => ({}),
    });
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue({
      scale: vi.fn(),
      clearRect: vi.fn(),
      fillRect: vi.fn(),
      fillStyle: "",
    } as unknown as CanvasRenderingContext2D);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("leaves vertical wheel and touch scrolling to the page", async () => {
    const onSeek = vi.fn();
    const audioEl = { currentTime: 10 } as HTMLAudioElement;
    const { container } = render(AudioWaveformFilmstrip, {
      peaks: [-1, 1, -0.5, 0.5],
      duration: 100,
      audioEl,
      onSeek,
    });

    await screen.findByRole("button", { name: "Scrub back" });
    const scrubber = container.querySelector(".touch-pan-y") as HTMLElement;
    expect(scrubber).toBeTruthy();
    const verticalWheel = new WheelEvent("wheel", {
      bubbles: true,
      cancelable: true,
      deltaX: 2,
      deltaY: 100,
    });

    scrubber.dispatchEvent(verticalWheel);

    expect(verticalWheel.defaultPrevented).toBe(false);
    expect(onSeek).not.toHaveBeenCalled();

    await fireEvent.wheel(scrubber, { deltaX: 360, deltaY: 0 });
    expect(onSeek).toHaveBeenCalledWith(20);
  });
});
