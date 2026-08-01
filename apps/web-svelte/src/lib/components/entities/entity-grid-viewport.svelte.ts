interface ContainedScrollHeightInput {
  bottomPadding?: number;
  minHeight?: number;
  top: number;
  viewportHeight: number;
}

/** Finds the nearest ancestor that establishes vertical scrolling for an element. */
export function findEntityGridScrollAncestor(element: Element): Element | null {
  let current: Element | null = element.parentElement;
  while (current && current !== document.body && current !== document.documentElement) {
    const overflowY = getComputedStyle(current).overflowY;
    if (overflowY === "auto" || overflowY === "scroll") return current;
    current = current.parentElement;
  }
  return null;
}

/**
 * Resolves the visual bottom edge available to the grid, excluding the scroll
 * container's reserved bottom padding when one owns the scroll surface.
 */
export function entityGridScrollContainerBottom(element: Element): number {
  const scrollAncestor = findEntityGridScrollAncestor(element);
  if (!scrollAncestor) return window.innerHeight;

  const styles = getComputedStyle(scrollAncestor);
  const bottomPadding = parseFloat(styles.paddingBottom) || 0;
  return scrollAncestor.getBoundingClientRect().bottom - bottomPadding;
}

/**
 * Compute the pixel height the contained grid viewport should occupy so the
 * sticky pagination strip lands at the bottom of the visible viewport while
 * leaving room for the outer page chrome above the grid. The grid no longer
 * traps wheel events at its scroll boundaries — callers should let
 * `overscroll-behavior: auto` handle scroll chaining to the outer page.
 */
export function computeContainedScrollHeight({
  bottomPadding = 24,
  minHeight = 320,
  top,
  viewportHeight,
}: ContainedScrollHeightInput): string {
  const visibleTop = Math.max(0, top);
  const available = Math.floor(viewportHeight - visibleTop - bottomPadding);
  return `${Math.max(minHeight, available)}px`;
}
