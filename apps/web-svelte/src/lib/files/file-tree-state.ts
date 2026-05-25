import type { FileEntry, FileRoot } from "$lib/api/prismedia";

export interface FileTreeNodeMeta {
  rootId: string;
  path: string;
  name: string;
  kind: string;
  treePath: string;
}

export function fileTreeRootPath(root: Pick<FileRoot, "id" | "label" | "path">, allRoots?: Pick<FileRoot, "id" | "label" | "path">[]): string {
  const label = root.label?.trim() || root.path?.split(/[\\/]/).filter(Boolean).at(-1) || "Library";
  if (allRoots) {
    const duplicates = allRoots.filter((other) => {
      const otherLabel = other.label?.trim() || other.path?.split(/[\\/]/).filter(Boolean).at(-1) || "Library";
      return otherLabel === label && other.id !== root.id;
    });
    if (duplicates.length > 0) return `${label} (${root.id.slice(0, 8)})`;
  }
  return label;
}

export function fileTreeEntryPath(rootTreePath: string, entry: Pick<FileEntry, "path">): string {
  if (!entry.path) return rootTreePath;
  const base = rootTreePath.endsWith("/") ? rootTreePath.slice(0, -1) : rootTreePath;
  return `${base}/${entry.path}`;
}

export function createFileTreeRegistry(roots: FileRoot[]): Map<string, FileTreeNodeMeta> {
  const registry = new Map<string, FileTreeNodeMeta>();
  for (const root of roots) {
    const treePath = `${fileTreeRootPath(root, roots)}/`;
    registry.set(treePath, {
      rootId: root.id,
      path: "",
      name: root.label || root.path,
      kind: "directory",
      treePath,
    });
  }
  return registry;
}

export function upsertFileTreeEntries(
  registry: Map<string, FileTreeNodeMeta>,
  rootTreePath: string,
  entries: FileEntry[],
): string[] {
  const treePaths: string[] = [];
  for (const entry of entries) {
    const basePath = fileTreeEntryPath(rootTreePath, entry);
    const treePath = entry.kind === "directory" ? `${basePath}/` : basePath;
    registry.set(treePath, {
      rootId: entry.rootId,
      path: entry.path,
      name: entry.name,
      kind: entry.kind,
      treePath,
    });
    treePaths.push(treePath);
  }
  return treePaths;
}

export interface ReconciledFileTree {
  treePaths: string[];
  loadedKeys: Set<string>;
  childPaths: string[];
}

export function reconcileFileTreeEntries(
  registry: Map<string, FileTreeNodeMeta>,
  treePaths: string[],
  loadedKeys: ReadonlySet<string>,
  rootTreePath: string,
  directory: Pick<FileTreeNodeMeta, "rootId" | "path">,
  entries: FileEntry[],
): ReconciledFileTree {
  const nextEntryPaths = new Set(entries.map((entry) => entry.path));
  const removedChildren = treePaths
    .map((treePath) => registry.get(treePath))
    .filter((meta): meta is FileTreeNodeMeta => Boolean(meta))
    .filter((meta) => meta.rootId === directory.rootId)
    .filter((meta) => parentPath(meta.path) === directory.path)
    .filter((meta) => !nextEntryPaths.has(meta.path));
  const removedSubtrees = removedChildren.map((meta) => meta.treePath);
  const removedPaths = removedChildren.map((meta) => meta.path);

  const removedTreePaths = new Set<string>();
  for (const subtree of removedSubtrees) {
    for (const treePath of treePaths) {
      if (isSameOrDescendantTreePath(treePath, subtree)) {
        removedTreePaths.add(treePath);
      }
    }
  }

  for (const treePath of removedTreePaths) {
    registry.delete(treePath);
  }

  const childPaths = upsertFileTreeEntries(registry, rootTreePath, entries);
  const childPathSet = new Set(childPaths);
  const nextTreePaths = [
    ...treePaths.filter((treePath) => !removedTreePaths.has(treePath)),
    ...childPaths.filter((treePath) => !treePaths.includes(treePath)),
  ];
  const nextLoadedKeys = new Set(
    [...loadedKeys].filter((key) => {
      const [, path = ""] = key.split(/:(.*)/s);
      return !removedPaths.some((removedPath) => path === removedPath || path.startsWith(`${removedPath}/`));
    }),
  );

  return {
    treePaths: nextTreePaths.filter((treePath, index, all) =>
      all.indexOf(treePath) === index && (registry.has(treePath) || childPathSet.has(treePath)),
    ),
    loadedKeys: nextLoadedKeys,
    childPaths,
  };
}

export function unloadedExpandedDirectories(
  treePaths: readonly string[],
  registry: Map<string, FileTreeNodeMeta>,
  loadedKeys: ReadonlySet<string>,
  isExpandedDirectory: (treePath: string) => boolean,
): FileTreeNodeMeta[] {
  return treePaths
    .map((treePath) => registry.get(treePath))
    .filter((meta): meta is FileTreeNodeMeta => Boolean(meta))
    .filter((meta) => meta.kind === "directory")
    .filter((meta) => !loadedKeys.has(`${meta.rootId}:${meta.path}`))
    .filter((meta) => isExpandedDirectory(meta.treePath));
}

function parentPath(path: string): string {
  return path.split("/").filter(Boolean).slice(0, -1).join("/");
}

function isSameOrDescendantTreePath(treePath: string, subtree: string): boolean {
  if (treePath === subtree) return true;
  return subtree.endsWith("/") && treePath.startsWith(subtree);
}
