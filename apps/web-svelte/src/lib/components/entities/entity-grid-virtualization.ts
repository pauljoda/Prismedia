import type { EntityGridViewMode } from "$lib/entities/entity-grid";

export interface EntityGridColumnRequest {
  mediaWall: boolean;
  scale: number;
  viewMode: EntityGridViewMode;
  viewportWidth: number;
}

export interface EntityGridRow<T> {
  index: number;
  startIndex: number;
  items: T[];
}

export interface EntityGridVirtualWindowRequest {
  estimatedRowHeight: number;
  overscan: number;
  rowCount: number;
  rowGap: number;
  rowHeights: Record<number, number>;
  scrollOffset: number;
  viewportHeight: number;
}

export interface EntityGridVirtualWindow {
  afterHeight: number;
  beforeHeight: number;
  endRow: number;
  startRow: number;
  totalHeight: number;
  visibleRowCount: number;
}

/** Number of page cards above which EntityGrid switches from full-page DOM rendering to row virtualization. */
export const ENTITY_GRID_VIRTUALIZATION_THRESHOLD = 60;
/** Extra scroll distance rendered above and below the visible viewport so fast scrolling has ready thumbnails. */
export const ENTITY_GRID_VIRTUAL_OVERSCAN_PX = 1_600;
/** Initial row estimate used until real row measurements arrive from ResizeObserver. */
export const ENTITY_GRID_ESTIMATED_ROW_HEIGHT = 360;
export const ENTITY_GRID_ROW_GAP_PX = 12;
export const ENTITY_GRID_MEDIA_WALL_ROW_GAP_PX = 8;

/** Mirrors EntityGrid's responsive CSS column rules so row virtualization slices cards on real row boundaries. */
export function resolveEntityGridColumnCount({
  mediaWall,
  scale,
  viewMode,
  viewportWidth,
}: EntityGridColumnRequest): number {
  if (viewMode === "list") return 1;

  const safeScale = Math.max(1, Math.floor(scale));
  if (mediaWall) return safeScale;
  if (viewportWidth >= 1024) return safeScale;
  if (viewportWidth >= 640) return Math.max(1, Math.min(safeScale, 4));
  return Math.max(1, Math.min(safeScale - 1, 4));
}

/** Groups a logical page of cards into stable rows for row-level virtualization. */
export function chunkEntityGridRows<T>(items: T[], columnCount: number): EntityGridRow<T>[] {
  const columns = Math.max(1, Math.floor(columnCount));
  const rows: EntityGridRow<T>[] = [];

  for (let startIndex = 0; startIndex < items.length; startIndex += columns) {
    rows.push({
      index: rows.length,
      startIndex,
      items: items.slice(startIndex, startIndex + columns),
    });
  }

  return rows;
}

/** Calculates the rendered row range and spacer heights for the current scroll position. */
export function computeEntityGridVirtualWindow({
  estimatedRowHeight,
  overscan,
  rowCount,
  rowGap,
  rowHeights,
  scrollOffset,
  viewportHeight,
}: EntityGridVirtualWindowRequest): EntityGridVirtualWindow {
  if (rowCount <= 0) {
    return {
      afterHeight: 0,
      beforeHeight: 0,
      endRow: 0,
      startRow: 0,
      totalHeight: 0,
      visibleRowCount: 0,
    };
  }

  const safeGap = Math.max(0, rowGap);
  const lowerBound = Math.max(0, scrollOffset - overscan);
  const upperBound = Math.max(lowerBound, scrollOffset + Math.max(0, viewportHeight) + overscan);
  const rowHeight = (index: number) => Math.max(1, rowHeights[index] ?? estimatedRowHeight);
  const rowStride = (index: number) => rowHeight(index) + (index < rowCount - 1 ? safeGap : 0);

  let startRow = 0;
  let beforeHeight = 0;
  while (startRow < rowCount && beforeHeight + rowStride(startRow) < lowerBound) {
    beforeHeight += rowStride(startRow);
    startRow += 1;
  }
  if (startRow >= rowCount) {
    startRow = rowCount - 1;
    beforeHeight = 0;
    for (let index = 0; index < startRow; index += 1) {
      beforeHeight += rowStride(index);
    }
  }

  let endRow = startRow;
  let renderedHeight = beforeHeight;
  while (endRow < rowCount && renderedHeight < upperBound) {
    renderedHeight += rowStride(endRow);
    endRow += 1;
  }

  if (endRow === startRow) {
    renderedHeight += rowStride(endRow);
    endRow = Math.min(rowCount, endRow + 1);
  }

  let totalHeight = 0;
  for (let index = 0; index < rowCount; index += 1) {
    totalHeight += rowStride(index);
  }

  return {
    afterHeight: Math.max(0, totalHeight - renderedHeight),
    beforeHeight,
    endRow,
    startRow,
    totalHeight,
    visibleRowCount: endRow - startRow,
  };
}
