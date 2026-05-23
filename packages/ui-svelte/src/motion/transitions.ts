import { fade, fly, scale, slide, crossfade, type FadeParams, type FlyParams, type ScaleParams, type SlideParams } from "svelte/transition";
import type { EasingFunction } from "svelte/transition";

// Mechanical bezier-easing helper. Svelte's built-in easings are good enough
// for most motion, but the design language defines specific cubic-bezier
// curves (see docs/design-language.md). This builds a JS-side easing
// function that mirrors a CSS cubic-bezier(x1, y1, x2, y2) curve.
function cubicBezier(x1: number, y1: number, x2: number, y2: number): EasingFunction {
  // Newton-Raphson on the parametric Bezier — accurate enough for UI motion.
  const NEWTON_ITER = 6;
  const NEWTON_MIN_SLOPE = 0.001;
  const SUBDIVISION_PRECISION = 1e-7;
  const SUBDIVISION_MAX = 12;

  const A = (a1: number, a2: number) => 1 - 3 * a2 + 3 * a1;
  const B = (a1: number, a2: number) => 3 * a2 - 6 * a1;
  const C = (a1: number) => 3 * a1;

  const calcBezier = (t: number, a1: number, a2: number) =>
    ((A(a1, a2) * t + B(a1, a2)) * t + C(a1)) * t;

  const getSlope = (t: number, a1: number, a2: number) =>
    3 * A(a1, a2) * t * t + 2 * B(a1, a2) * t + C(a1);

  function tForX(x: number): number {
    let t = x;
    for (let i = 0; i < NEWTON_ITER; i++) {
      const slope = getSlope(t, x1, x2);
      if (slope < NEWTON_MIN_SLOPE) break;
      const cx = calcBezier(t, x1, x2) - x;
      t -= cx / slope;
    }
    let lo = 0;
    let hi = 1;
    t = x;
    for (let i = 0; i < SUBDIVISION_MAX; i++) {
      const cx = calcBezier(t, x1, x2) - x;
      if (Math.abs(cx) < SUBDIVISION_PRECISION) return t;
      if (cx > 0) hi = t;
      else lo = t;
      t = (hi + lo) / 2;
    }
    return t;
  }

  return (t: number) => {
    if (t <= 0) return 0;
    if (t >= 1) return 1;
    return calcBezier(tForX(t), y1, y2);
  };
}

export const ease = {
  default: cubicBezier(0.4, 0, 0.2, 1),
  enter: cubicBezier(0, 0, 0.2, 1),
  exit: cubicBezier(0.4, 0, 1, 1),
  mechanical: cubicBezier(0.25, 0, 0.25, 1),
};

export const dur = {
  fast: 80,
  normal: 160,
  moderate: 240,
  slow: 380,
} as const;

// Wrapped helpers — call these directly in components (e.g. transition:fadeIn)
// so we don't repeat duration/easing tokens at every call site.

export function fadeIn(node: Element, opts: FadeParams = {}) {
  return fade(node, { duration: dur.moderate, easing: ease.enter, ...opts });
}

export function fadeOut(node: Element, opts: FadeParams = {}) {
  return fade(node, { duration: dur.normal, easing: ease.exit, ...opts });
}

export function fadeQuick(node: Element, opts: FadeParams = {}) {
  return fade(node, { duration: dur.fast, easing: ease.default, ...opts });
}

export function flyUp(node: Element, opts: FlyParams = {}) {
  return fly(node, { y: 12, duration: dur.moderate, easing: ease.enter, ...opts });
}

export function flyDown(node: Element, opts: FlyParams = {}) {
  return fly(node, { y: -12, duration: dur.moderate, easing: ease.enter, ...opts });
}

export function slideUp(node: Element, opts: FlyParams = {}) {
  return fly(node, { y: 24, duration: dur.moderate, easing: ease.enter, ...opts });
}

export function sheetUp(node: Element, opts: FlyParams = {}) {
  // Mobile bottom-sheet entrance — translateY(100%) → 0, mechanical easing
  return fly(node, { y: 200, duration: 280, easing: ease.mechanical, opacity: 1, ...opts });
}

export function scaleIn(node: Element, opts: ScaleParams = {}) {
  return scale(node, { start: 0.97, opacity: 0, duration: dur.moderate, easing: ease.enter, ...opts });
}

export function scaleChip(node: Element, opts: ScaleParams = {}) {
  return scale(node, { start: 0.85, opacity: 0, duration: dur.fast, easing: ease.enter, ...opts });
}

export function slideX(node: Element, opts: SlideParams = {}) {
  return slide(node, { axis: "x", duration: dur.moderate, easing: ease.mechanical, ...opts });
}

// Shared-element crossfade for thumbnail→lightbox transitions.
// Use as: out:sendThumb={{ key: id }} on the source, in:receiveThumb={{ key: id }} on the destination.
export const [sendThumb, receiveThumb] = crossfade({
  duration: dur.moderate,
  easing: ease.enter,
  fallback(node) {
    return scaleIn(node);
  },
});

// Honors the user's OS-level reduced-motion preference.
export function prefersReducedMotion(): boolean {
  if (typeof window === "undefined" || !window.matchMedia) return false;
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}
