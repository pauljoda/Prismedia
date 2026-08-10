import type { AcquisitionItemTone } from "$lib/requests/acquisition-list-item";
import { numberValue } from "$lib/utils/format";
import type { DownloadManagerEntry, DownloadTreeNode } from "./download-tree";

/** Columns owned by the Downloads tree table, in visible order after the fixed selection column. */
export const DOWNLOAD_TABLE_COLUMNS = {
  entity: { label: "Entity", defaultWidth: 320, minWidth: 220, maxWidth: 1600 },
  size: { label: "Size", defaultWidth: 112, minWidth: 84, maxWidth: 240 },
  progress: { label: "Progress", defaultWidth: 150, minWidth: 112, maxWidth: 280 },
  status: { label: "Status", defaultWidth: 170, minWidth: 120, maxWidth: 300 },
  speed: { label: "Speed", defaultWidth: 112, minWidth: 88, maxWidth: 220 },
  eta: { label: "ETA", defaultWidth: 88, minWidth: 72, maxWidth: 180 },
  peers: { label: "Seeds / Peers", defaultWidth: 100, minWidth: 88, maxWidth: 200 },
  updated: { label: "Updated", defaultWidth: 80, minWidth: 68, maxWidth: 160 },
} as const;

export type DownloadColumnKey = keyof typeof DOWNLOAD_TABLE_COLUMNS;
export type DownloadSortDirection = "asc" | "desc";
export type DownloadColumnWidths = Record<DownloadColumnKey, number>;

export const DOWNLOAD_SELECT_COLUMN_WIDTH = 36;
export const DOWNLOAD_TABLE_COLUMN_KEYS = Object.keys(DOWNLOAD_TABLE_COLUMNS) as DownloadColumnKey[];
export const DEFAULT_DOWNLOAD_COLUMN_WIDTHS = Object.fromEntries(
  Object.entries(DOWNLOAD_TABLE_COLUMNS).map(([key, definition]) => [key, definition.defaultWidth]),
) as DownloadColumnWidths;

/** Clamps a persisted or dragged width to the owning column's usable range. */
export function clampDownloadColumnWidth(key: DownloadColumnKey, width: number): number {
  const definition = DOWNLOAD_TABLE_COLUMNS[key];
  return Math.min(definition.maxWidth, Math.max(definition.minWidth, Math.round(width)));
}

/** CSS grid track declaration shared by the header and every recursive tree row. */
export function downloadColumnTemplate(widths: DownloadColumnWidths): string {
  return `${DOWNLOAD_SELECT_COLUMN_WIDTH}px ${DOWNLOAD_TABLE_COLUMN_KEYS
    .map((key) => {
      return key === "entity" ? `minmax(${widths[key]}px, 1fr)` : `${widths[key]}px`;
    })
    .join(" ")}`;
}

/** Total scrollable grid width for the current column widths. */
export function downloadTableWidth(widths: DownloadColumnWidths): number {
  return DOWNLOAD_SELECT_COLUMN_WIDTH + Object.values(widths).reduce((sum, width) => sum + width, 0);
}

const tonePriority: Record<AcquisitionItemTone, number> = {
  failed: 8,
  attention: 7,
  cleanup: 6,
  downloading: 5,
  searching: 4,
  queued: 3,
  muted: 2,
  done: 1,
};

function entrySortValue(entry: DownloadManagerEntry, key: DownloadColumnKey): string | number {
  switch (key) {
    case "entity": return entry.item.title.toLocaleLowerCase();
    case "size": return numberValue(entry.row.totalSizeBytes) ?? -1;
    case "progress": return entry.item.progress ?? -1;
    case "status": return tonePriority[entry.item.tone];
    case "speed": return numberValue(entry.row.downloadSpeedBytesPerSecond) ?? -1;
    case "eta": return numberValue(entry.row.etaSeconds) ?? -1;
    case "peers": return (numberValue(entry.row.seeds) ?? 0) + (numberValue(entry.row.peers) ?? 0);
    case "updated": return Date.parse(entry.row.updatedAt) || 0;
  }
}

function nodeSortValue(
  node: DownloadTreeNode,
  entriesById: ReadonlyMap<string, DownloadManagerEntry>,
  key: DownloadColumnKey,
): string | number {
  if (key === "entity") return node.title.toLocaleLowerCase();
  const entries = node.descendantEntryIds
    .map((id) => entriesById.get(id))
    .filter((entry): entry is DownloadManagerEntry => entry !== undefined);
  const values = entries.map((entry) => entrySortValue(entry, key)).filter((value): value is number => typeof value === "number");
  if (values.length === 0) return -1;
  if (key === "progress") return values.reduce((sum, value) => sum + value, 0) / values.length;
  if (key === "size" || key === "speed" || key === "peers") return values.reduce((sum, value) => sum + value, 0);
  return Math.max(...values);
}

function compareValues(a: string | number, b: string | number, direction: DownloadSortDirection): number {
  const result = typeof a === "string" && typeof b === "string"
    ? a.localeCompare(b, undefined, { numeric: true })
    : Number(a) - Number(b);
  return direction === "asc" ? result : -result;
}

/**
 * Sorts every sibling set without flattening the Entity hierarchy. Container values aggregate their
 * descendant transfers, while multiple acquisitions attached to one Entity sort within that node.
 */
export function sortDownloadTree(
  nodes: DownloadTreeNode[],
  entriesById: ReadonlyMap<string, DownloadManagerEntry>,
  key: DownloadColumnKey,
  direction: DownloadSortDirection,
): DownloadTreeNode[] {
  return nodes
    .map((node) => ({
      ...node,
      directEntryIds: [...node.directEntryIds].sort((a, b) => {
        const aEntry = entriesById.get(a);
        const bEntry = entriesById.get(b);
        if (!aEntry || !bEntry) return 0;
        return compareValues(entrySortValue(aEntry, key), entrySortValue(bEntry, key), direction);
      }),
      children: sortDownloadTree(node.children, entriesById, key, direction),
    }))
    .sort((a, b) => compareValues(
      nodeSortValue(a, entriesById, key),
      nodeSortValue(b, entriesById, key),
      direction,
    ));
}
