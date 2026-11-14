/** Scroll offset inside which the page counts as "at the top" for auto-collapse decisions. */
const TOP_ZONE_PX = 48;
/** Smallest downward movement that compacts the toolbar, filtering out scroll jitter. */
const COLLAPSE_DELTA_PX = 8;
/**
 * How long scroll events are ignored after an automatic change. The secondary rows slide for
 * 200ms, and scroll anchoring can shift the scroll position while they do; acting on those shifts
 * would flip the toolbar back and forth.
 */
const SETTLE_MS = 250;

/**
 * Owns the toolbar's transient secondary-row collapse state.
 *
 * Manual toggles are persisted through the supplied callback and permanently pin
 * the current view for this mount. An untouched toolbar compacts after a meaningful
 * downward scroll and expands again when scrolled back to the top.
 */
export class EntityGridToolbarCollapseController {
  /** Whether the toolbar's filter and bulk-selection rows are currently hidden. */
  barsCollapsed = $state(false);

  #collapsePinned: boolean;
  #onManualChange: (collapsed: boolean) => void;

  constructor(
    initialBarsCollapsed: boolean,
    onManualChange: (collapsed: boolean) => void,
  ) {
    this.barsCollapsed = initialBarsCollapsed;
    this.#collapsePinned = initialBarsCollapsed;
    this.#onManualChange = onManualChange;
  }

  /** Toggles and persists the collapse preference, preventing later scroll changes. */
  toggle(): void {
    this.barsCollapsed = !this.barsCollapsed;
    this.#collapsePinned = true;
    this.#onManualChange(this.barsCollapsed);
  }

  /**
   * Starts scroll-driven collapsing and returns the listener cleanup.
   * It is invoked from the toolbar component's mount lifecycle, so it never
   * subscribes while rendering on the server.
   */
  connectScroll(): () => void {
    let lastY = window.scrollY;
    let settleUntil = 0;

    const scrollTopOf = (target: EventTarget | null): number =>
      target instanceof HTMLElement ? target.scrollTop : window.scrollY;

    const onScroll = (event: Event) => {
      if (this.#collapsePinned) return;
      const y = scrollTopOf(event.target);
      const delta = y - lastY;
      lastY = y;
      if (performance.now() < settleUntil) return;

      const next = this.barsCollapsed
        ? !(delta < 0 && y <= TOP_ZONE_PX)
        : delta > COLLAPSE_DELTA_PX && y > TOP_ZONE_PX;
      if (next === this.barsCollapsed) return;
      this.barsCollapsed = next;
      settleUntil = performance.now() + SETTLE_MS;
    };

    window.addEventListener("scroll", onScroll, { capture: true, passive: true });
    return () => window.removeEventListener("scroll", onScroll, { capture: true });
  }
}
