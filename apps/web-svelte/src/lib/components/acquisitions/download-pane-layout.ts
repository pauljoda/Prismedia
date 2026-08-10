export const DEFAULT_DOWNLOAD_DETAIL_SHARE = 0.4;
export const DOWNLOAD_PANE_SPLITTER_PX = 12;
export const MIN_DOWNLOAD_LIST_HEIGHT_PX = 208;
export const MIN_DOWNLOAD_DETAIL_HEIGHT_PX = 192;

/** Keeps both download panes usable while allowing the inspector to claim most of a tall workspace. */
export function clampDownloadDetailShare(share: number, totalHeight: number): number {
  const safeShare = Math.min(0.85, Math.max(0.15, share));
  const availableHeight = totalHeight - DOWNLOAD_PANE_SPLITTER_PX;
  if (availableHeight <= MIN_DOWNLOAD_LIST_HEIGHT_PX + MIN_DOWNLOAD_DETAIL_HEIGHT_PX) {
    return safeShare;
  }
  const minimumShare = MIN_DOWNLOAD_DETAIL_HEIGHT_PX / availableHeight;
  const maximumShare = 1 - MIN_DOWNLOAD_LIST_HEIGHT_PX / availableHeight;
  return Math.min(maximumShare, Math.max(minimumShare, safeShare));
}

/** Converts a dragged inspector height into the fractional grid share persisted across viewports. */
export function downloadDetailShareForHeight(detailHeight: number, totalHeight: number): number {
  const availableHeight = Math.max(1, totalHeight - DOWNLOAD_PANE_SPLITTER_PX);
  return clampDownloadDetailShare(detailHeight / availableHeight, totalHeight);
}
