import { render, screen } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import PrismediaLoadingMark from "./PrismediaLoadingMark.svelte";
import {
  PRISM_LOADING_CYCLE_SECONDS,
  prismLoadingFrameAt,
  prismLoadingFrameAtElapsed,
  prismLoadingGeometry,
  reducedMotionPrismLoadingFrame,
} from "./prism-loading-frame";

describe("prism loading animation", () => {
  it("matches the native 2.8 second phase boundaries", () => {
    expect(prismLoadingFrameAt(0.04).incomingLightProgress).toBe(0);
    expect(prismLoadingFrameAt(0.38).incomingLightProgress).toBe(1);
    expect(prismLoadingFrameAt(0.43).impactGlowOpacity).toBe(1);
    expect(prismLoadingFrameAt(0.54)).toMatchObject({
      prismColorProgress: 1,
      impactGlowOpacity: 0,
    });
    expect(prismLoadingFrameAt(0.84).spectrumProgress).toBe(1);
    expect(prismLoadingFrameAt(0.9)).toMatchObject({
      prismColorProgress: 1,
      spectrumOpacity: 1,
    });
    expect(prismLoadingFrameAt(1)).toMatchObject({
      prismColorProgress: 0,
      spectrumOpacity: 0,
    });
  });

  it("uses native smoothstep easing rather than linear interpolation", () => {
    expect(prismLoadingFrameAt(0.125).incomingLightProgress).toBeCloseTo(0.15625, 12);
  });

  it("wraps elapsed time at the native cycle duration", () => {
    expect(prismLoadingFrameAtElapsed(PRISM_LOADING_CYCLE_SECONDS)).toEqual(
      prismLoadingFrameAtElapsed(0),
    );
    const negativeFrame = prismLoadingFrameAtElapsed(-0.7);
    const wrappedFrame = prismLoadingFrameAtElapsed(2.1);
    expect(negativeFrame.incomingLightOpacity).toBeCloseTo(wrappedFrame.incomingLightOpacity, 12);
    expect(negativeFrame.spectrumProgress).toBeCloseTo(wrappedFrame.spectrumProgress, 12);
  });

  it("keeps reduced motion on the native static colored-prism frame", () => {
    expect(reducedMotionPrismLoadingFrame).toEqual({
      incomingLightProgress: 1,
      incomingLightOpacity: 0,
      prismColorProgress: 1,
      spectrumProgress: 1,
      spectrumOpacity: 0.72,
      impactGlowOpacity: 0,
    });
  });

  it("matches the native 760 by 128 geometry and sends beams outside the viewport", () => {
    const geometry = prismLoadingGeometry(760, 128, 72);

    expect(geometry.prism).toEqual({ x: 344, y: 30.8125, width: 72, height: 66.375 });
    expect(geometry.entry).toEqual({ x: 360.7625, y: 70.075 });
    expect(geometry.impact).toEqual({ x: 380, y: 70.075 });
    expect(geometry.incomingStart).toEqual({ x: -40, y: 120 });
    expect(geometry.spectrumTargetX).toBe(800);
  });

  it("caps the mark inside narrow containers", () => {
    expect(prismLoadingGeometry(48, 128, 72).prism).toEqual({
      x: 12,
      y: 52.9375,
      width: 24,
      height: 22.125,
    });
  });
});

describe("PrismediaLoadingMark", () => {
  beforeEach(() => {
    vi.stubGlobal("requestAnimationFrame", vi.fn(() => 1));
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(private readonly callback: ResizeObserverCallback) {}

        observe(target: Element) {
          this.callback(
            [{ target, contentRect: { width: 760, height: 128 } } as ResizeObserverEntry],
            this as unknown as ResizeObserver,
          );
        }

        disconnect() {}
        unobserve() {}
      },
    );
    Object.defineProperty(window, "matchMedia", {
      configurable: true,
      value: vi.fn().mockReturnValue({
        matches: false,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }),
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders an accessible status with a decorative transparent SVG", () => {
    const { container } = render(PrismediaLoadingMark, {
      props: { label: "Loading library", showLabel: true, previewProgress: 0.44 },
    });

    expect(screen.getByRole("status", { name: "Loading library" })).toHaveAttribute("aria-busy", "true");
    expect(screen.getByText("Loading library")).toBeVisible();
    expect(container.querySelector("svg")).toHaveAttribute("aria-hidden", "true");
    expect(container.querySelector("svg")).toHaveAttribute("focusable", "false");
    expect(container.querySelectorAll(".spectrum > .spectrum-band:not(.spectrum-band-glow)")).toHaveLength(7);
    expect(container.querySelector('.prism-neutral[href="/brand/prismedia-prism-neutral.png"]')).toBeInTheDocument();
    expect(container.querySelector('.prism-color[href="/brand/prismedia-prism-color.png"]')).toBeInTheDocument();
  });

  it("uses the reduced-motion static frame when requested by the browser", () => {
    vi.mocked(window.matchMedia).mockReturnValue({
      matches: true,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    } as unknown as MediaQueryList);

    const { container } = render(PrismediaLoadingMark, { props: { label: "Loading" } });

    expect(container.querySelector("svg")).toHaveAttribute("data-reduced-motion", "true");
    expect(container.querySelector(".prism-color")).toHaveAttribute("opacity", "1");
    expect(container.querySelector(".spectrum")).toHaveAttribute("opacity", "0.72");
    expect(requestAnimationFrame).not.toHaveBeenCalled();
  });
});
