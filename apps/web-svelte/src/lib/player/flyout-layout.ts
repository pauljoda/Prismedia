/**
 * Layout for mobile `position: fixed` player menus (subtitles, quality).
 * Flips up or down based on which side of the trigger has more room, and caps
 * height so the panel scrolls inside the viewport.
 */
export interface PlayerMobileFlyoutLayout {
  top: string;
  bottom: string;
  left: string;
  right: string;
  maxHeight: string;
  width?: string;
  minWidth?: string;
  maxWidth?: string;
}

/** Safe-area insets in CSS px (iOS notches, home bar). */
export interface SafeAreaInsets {
  top: number;
  bottom: number;
}

/**
 * Resolve `env(safe-area-inset-*)` from the layout so JS math matches what fixed
 * UI actually reserves.
 */
export function getSafeAreaInsets(): SafeAreaInsets {
  if (typeof document === "undefined" || !document.body) {
    return { top: 0, bottom: 0 };
  }
  return {
    top: measureSafeEdge("paddingTop", "env(safe-area-inset-top, 0px)"),
    bottom: measureSafeEdge("paddingBottom", "env(safe-area-inset-bottom, 0px)"),
  };
}

function measureSafeEdge(prop: "paddingTop" | "paddingBottom", value: string): number {
  const d = document.createElement("div");
  d.setAttribute(
    "style",
    `box-sizing:content-box;position:fixed;left:-9999px;top:0;width:0;height:0;` +
      `margin:0;border:0;padding:0;overflow:hidden;visibility:hidden;` +
      `${prop}:${value};`,
  );
  document.body.appendChild(d);
  const px = d.offsetHeight;
  document.body.removeChild(d);
  return px;
}

/** Shaves a couple of CSS px so borders/subpixel don’t nudge the panel out of the slot. */
const MAX_HEIGHT_FUDGE_PX = 2;

export function layoutPlayerMobileFlyout(
  trigger: DOMRect,
  options: {
    /**
     * Height of the fixed containing block. Must match
     * `getBoundingClientRect` / `position: fixed` (layout viewport) — i.e. use
     * `window.innerHeight`, not `visualViewport.height`, or coordinates diverge
     * and the panel can clip a few pixels at an edge.
     */
    vh: number;
    /** Cap as a fraction of vh (e.g. 0.6 for 60%). */
    maxHeightVh: number;
    /** Extra gap between trigger and flyout. */
    gap?: number;
    /**
     * Minimum clear margin from the layout viewport top/bottom (in addition to
     * `safeAreaInsets` when you pass that).
     */
    padTop?: number;
    padBottom?: number;
    /** Merged with pad* for space calculations. Defaults via {@link getSafeAreaInsets}. */
    safeAreaInsets?: SafeAreaInsets;
    /** Width of the fixed containing block. Required when anchoring to a trigger. */
    vw?: number;
    /** Keep the flyout at least this far from the viewport's left/right edges. */
    gutter?: number;
    /**
     * Preferred menu width for trigger-anchored desktop layouts. When omitted,
     * the menu stretches between the left/right gutters.
     */
    preferredWidth?: number;
    /** Minimum menu width for trigger-anchored desktop layouts. */
    minWidth?: number;
  },
): PlayerMobileFlyoutLayout {
  const {
    vh,
    maxHeightVh,
    gap = 8,
    padTop: padTopUser = 8,
    padBottom: padBottomUser = 8,
    safeAreaInsets: safe = getSafeAreaInsets(),
    vw,
    gutter = 12,
    preferredWidth,
    minWidth,
  } = options;

  const effPadTop = padTopUser + safe.top;
  const effPadBottom = padBottomUser + safe.bottom;

  const cap = maxHeightVh * vh;

  const spaceUp = Math.max(0, trigger.top - gap - effPadTop);
  const spaceDown = Math.max(0, vh - trigger.bottom - gap - effPadBottom);

  const openUp = spaceUp >= spaceDown;
  const hAvailable = openUp ? spaceUp : spaceDown;
  const maxHeight = Math.max(1, Math.min(cap, hAvailable) - MAX_HEIGHT_FUDGE_PX);

  const layout: PlayerMobileFlyoutLayout = {
    top: "auto",
    bottom: "auto",
    left: `${gutter}px`,
    right: `${gutter}px`,
    maxHeight: `${Math.floor(maxHeight)}px`,
  };

  if (openUp) {
    const fromBottom = Math.max(0, vh - (trigger.top - gap));
    layout.bottom = `${Math.floor(fromBottom)}px`;
  } else {
    layout.top = `${Math.floor(trigger.bottom + gap)}px`;
  }

  if (typeof vw === "number" && typeof preferredWidth === "number") {
    const availableWidth = Math.max(1, vw - gutter * 2);
    const width = Math.min(preferredWidth, availableWidth);
    const maxLeft = Math.max(gutter, vw - gutter - width);
    const left = Math.min(Math.max(gutter, trigger.right - width), maxLeft);
    layout.left = `${Math.floor(left)}px`;
    layout.right = "auto";
    layout.width = `${Math.floor(width)}px`;
    layout.maxWidth = `${Math.floor(availableWidth)}px`;
    if (typeof minWidth === "number") {
      layout.minWidth = `${Math.floor(Math.min(minWidth, width))}px`;
    }
  }

  return layout;
}

export function playerFlyoutStyleToString(style: PlayerMobileFlyoutLayout): string {
  return Object.entries(style)
    .filter(([, value]) => value !== undefined)
    .map(([property, value]) => `${toKebabCase(property)}:${value}`)
    .join(";");
}

function toKebabCase(value: string): string {
  return value.replace(/[A-Z]/g, (char) => `-${char.toLowerCase()}`);
}
