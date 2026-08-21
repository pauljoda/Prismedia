import {
  CAPABILITY_KIND,
  type EntityKindCode,
} from "$lib/entities/entity-codes";
import { ENTITY_SEQUENCE_ROLE } from "$lib/api/generated/codes";
import { getCapability } from "$lib/api/capabilities";
import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
import { getChildIds } from "$lib/entities/entity-children";
import { fetchOrderedEntityThumbnails } from "$lib/entities/entity-relationship-thumbnails";
import type { EntityThumbnail } from "$lib/api/generated/model";

export interface OrderedEntitySequence {
  root: EntityCardFull;
  items: EntityThumbnail[];
}

/**
 * Resolves a capability-declared item through its allowed container ancestors, then flattens the
 * root's direct and one-level grouped items into semantic order. This is shared sequence behavior;
 * it does not know about series, seasons, volumes, episodes, or chapters.
 */
export async function resolveOrderedEntitySequence(
  selected: EntityCardFull,
  options?: { signal?: AbortSignal },
): Promise<OrderedEntitySequence | null> {
  const sequence = getCapability(selected.capabilities, CAPABILITY_KIND.orderedSequence);
  if (!sequence || sequence.role !== ENTITY_SEQUENCE_ROLE.item) return null;

  const containerKinds = new Set<EntityKindCode>(sequence.containerKinds);
  let root = selected;
  let parentId = selected.parentEntityId;
  while (parentId) {
    const parent = await fetchEntity(parentId, options);
    if (!containerKinds.has(parent.kind)) break;
    root = parent;
    parentId = parent.parentEntityId;
  }

  if (selected.parentEntityId === root.id) {
    const items = await fetchOrderedEntityThumbnails(
      getChildIds(root, sequence.itemKind),
      options,
    );
    items.sort(compareSequenceItems);
    return { root, items };
  }

  const nestedContainerIds = [...containerKinds]
    .filter((kind) => kind !== root.kind)
    .flatMap((kind) => getChildIds(root, kind));
  const nestedContainers = await Promise.all(
    nestedContainerIds.map((id) => fetchEntity(id, options)),
  );
  nestedContainers.sort(compareSequenceItems);

  const items: EntityThumbnail[] = [];
  for (const container of nestedContainers) {
    const children = await fetchOrderedEntityThumbnails(
      getChildIds(container, sequence.itemKind),
      options,
    );
    children.sort(compareSequenceItems);
    items.push(...children);
  }

  return { root, items };
}

function compareSequenceItems(
  left: Pick<EntityThumbnail, "id" | "sortOrder" | "title">,
  right: Pick<EntityThumbnail, "id" | "sortOrder" | "title">,
): number {
  const leftOrder = left.sortOrder == null ? Number.MAX_SAFE_INTEGER : Number(left.sortOrder);
  const rightOrder = right.sortOrder == null ? Number.MAX_SAFE_INTEGER : Number(right.sortOrder);
  if (leftOrder !== rightOrder) return leftOrder - rightOrder;
  const title = left.title.localeCompare(right.title, undefined, { numeric: true, sensitivity: "base" });
  return title || left.id.localeCompare(right.id);
}
