/**
 * Generic hierarchical tree builder for any entity with `id` + `parentId`.
 * Used by gallery browser, audio library browser, and any future hierarchy view.
 */

export interface TreeNode<T> {
  data: T;
  children: TreeNode<T>[];
}

interface Hierarchical {
  id: string;
  parentId?: string | null;
}

/**
 * Builds a tree from a flat list of entities with `id` and `parentId` fields.
 * Entities whose `parentId` references a node not in the list become roots.
 *
 * @param items  Flat array of entities
 * @param sortBy Optional comparator to sort children at each level
 */
export function buildHierarchyTree<T extends Hierarchical>(
  items: T[],
  sortBy?: (a: T, b: T) => number,
): TreeNode<T>[] {
  const nodeMap = new Map<string, TreeNode<T>>();
  const roots: TreeNode<T>[] = [];

  // Create nodes
  for (const item of items) {
    nodeMap.set(item.id, { data: item, children: [] });
  }

  // Build tree
  for (const item of items) {
    const node = nodeMap.get(item.id)!;
    if (item.parentId && nodeMap.has(item.parentId)) {
      nodeMap.get(item.parentId)!.children.push(node);
    } else {
      roots.push(node);
    }
  }

  // Sort children recursively
  if (sortBy) {
    const sortChildren = (nodes: TreeNode<T>[]) => {
      nodes.sort((a, b) => sortBy(a.data, b.data));
      for (const node of nodes) {
        sortChildren(node.children);
      }
    };
    sortChildren(roots);
  }

  return roots;
}
