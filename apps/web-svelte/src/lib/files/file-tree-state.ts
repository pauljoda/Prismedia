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

