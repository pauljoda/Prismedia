/**
 * Owns the toolbar's transient secondary-row collapse state.
 *
 * Manual toggles are persisted through the supplied callback and permanently pin
 * the current view for this mount. Scrolling only performs the one-way compacting
 * transition for an untouched toolbar.
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
   * Starts one-way scroll-driven collapsing and returns the listener cleanup.
   * It is invoked from the toolbar component's mount lifecycle, so it never
   * subscribes while rendering on the server.
   */
  connectScroll(): () => void {
    let lastY = window.scrollY;

    const scrollTopOf = (target: EventTarget | null): number =>
      target instanceof HTMLElement ? target.scrollTop : window.scrollY;

    const onScroll = (event: Event) => {
      if (this.#collapsePinned || this.barsCollapsed) return;
      const y = scrollTopOf(event.target);
      const delta = y - lastY;
      lastY = y;
      if (delta > 8 && y > 48) this.barsCollapsed = true;
    };

    window.addEventListener("scroll", onScroll, { capture: true, passive: true });
    return () => window.removeEventListener("scroll", onScroll, { capture: true });
  }
}
