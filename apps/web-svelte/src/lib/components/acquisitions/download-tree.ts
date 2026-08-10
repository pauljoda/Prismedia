import type { EntityThumbnail } from "$lib/api/generated/model";
import type { AcquisitionListItem } from "$lib/requests/acquisition-list-item";
import type { DownloadQueueItemView } from "$lib/api/generated/model";
import { numberValue } from "$lib/utils/format";

/** One live transfer paired with the normalized presentation model used by acquisition surfaces. */
export interface DownloadManagerEntry {
  row: DownloadQueueItemView;
  item: AcquisitionListItem;
}

/** One real Entity node in the Downloads hierarchy, or one fallback node for an unbound acquisition. */
export interface DownloadTreeNode {
  key: string;
  entityId: string | null;
  title: string;
  thumbnail: EntityThumbnail | null;
  directEntryIds: string[];
  descendantEntryIds: string[];
  children: DownloadTreeNode[];
  activityOrder: number;
}

interface MutableDownloadTreeNode extends Omit<DownloadTreeNode, "children" | "descendantEntryIds"> {
  children: MutableDownloadTreeNode[];
  descendantEntryIds: string[];
}

function compareNodes(a: MutableDownloadTreeNode, b: MutableDownloadTreeNode): number {
  const aOrder = numberValue(a.thumbnail?.sortOrder);
  const bOrder = numberValue(b.thumbnail?.sortOrder);
  if (aOrder !== null && bOrder !== null && aOrder !== bOrder) return aOrder - bOrder;
  if (aOrder !== null && bOrder === null) return -1;
  if (aOrder === null && bOrder !== null) return 1;
  if (a.activityOrder !== b.activityOrder) return a.activityOrder - b.activityOrder;
  return a.title.localeCompare(b.title, undefined, { numeric: true });
}

function finalizeNode(node: MutableDownloadTreeNode): DownloadTreeNode {
  node.children.sort(compareNodes);
  const children = node.children.map(finalizeNode);
  const descendantEntryIds = [
    ...node.directEntryIds,
    ...children.flatMap((child) => child.descendantEntryIds),
  ];
  return { ...node, children, descendantEntryIds };
}

/**
 * Builds the real Entity hierarchy above the active queue. Parent thumbnails are expected to have been
 * resolved by the caller. A download that is not yet bound to an Entity remains independently usable.
 */
export function buildDownloadTree(
  entries: DownloadManagerEntry[],
  thumbnails: ReadonlyMap<string, EntityThumbnail>,
): DownloadTreeNode[] {
  const nodes = new Map<string, MutableDownloadTreeNode>();
  const roots: MutableDownloadTreeNode[] = [];

  function ensureEntityNode(entityId: string, activityOrder: number): MutableDownloadTreeNode | null {
    const thumbnail = thumbnails.get(entityId);
    if (!thumbnail) return null;
    const existing = nodes.get(entityId);
    if (existing) {
      existing.activityOrder = Math.min(existing.activityOrder, activityOrder);
      return existing;
    }
    const node: MutableDownloadTreeNode = {
      key: `entity:${entityId}`,
      entityId,
      title: thumbnail.title,
      thumbnail,
      directEntryIds: [],
      descendantEntryIds: [],
      children: [],
      activityOrder,
    };
    nodes.set(entityId, node);
    return node;
  }

  entries.forEach((entry, activityOrder) => {
    const entityId = entry.row.entityId;
    const leaf = entityId ? ensureEntityNode(entityId, activityOrder) : null;
    if (!leaf) {
      roots.push({
        key: `acquisition:${entry.item.id}`,
        entityId: null,
        title: entry.item.title,
        thumbnail: null,
        directEntryIds: [entry.item.id],
        descendantEntryIds: [entry.item.id],
        children: [],
        activityOrder,
      });
      return;
    }

    leaf.directEntryIds.push(entry.item.id);
    let child = leaf;
    const visited = new Set([entityId!]);
    while (child.thumbnail?.parentEntityId && !visited.has(child.thumbnail.parentEntityId)) {
      const parentId = child.thumbnail.parentEntityId;
      const parent = ensureEntityNode(parentId, activityOrder);
      if (!parent) break;
      visited.add(parentId);
      if (!parent.children.includes(child)) parent.children.push(child);
      child = parent;
    }
  });

  const attached = new Set<string>();
  for (const node of nodes.values()) {
    for (const child of node.children) attached.add(child.key);
  }
  for (const node of nodes.values()) {
    if (!attached.has(node.key)) roots.push(node);
  }

  roots.sort(compareNodes);
  return roots.map(finalizeNode);
}

/** Returns every expandable Entity key in a tree. */
export function expandableDownloadNodeKeys(nodes: DownloadTreeNode[]): string[] {
  return nodes.flatMap((node) => [
    ...(node.children.length > 0 || node.directEntryIds.length > 1 ? [node.key] : []),
    ...expandableDownloadNodeKeys(node.children),
  ]);
}
