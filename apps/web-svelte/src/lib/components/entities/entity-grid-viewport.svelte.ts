interface ContainedScrollHeightInput {
  bottomPadding?: number;
  minHeight?: number;
  top: number;
  viewportHeight: number;
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
