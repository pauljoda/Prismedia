import { describe, expect, it, vi } from "vitest";
import {
  createFileTreeRegistry,
  fileTreeEntryPath,
  fileTreeRootPath,
  reconcileFileTreeEntries,
  unloadedExpandedDirectories,
  upsertFileTreeEntries,
} from "./file-tree-state";
import type { FileEntry, FileRoot } from "$lib/api/generated/model";

describe("file tree state", () => {
  const root: FileRoot = {
    id: "root-alpha-1234",
    label: "Movies",
    path: "/media/movies",
    enabled: true,
  };

  it("maps display tree paths back to root ids and relative paths", () => {
    const registry = createFileTreeRegistry([root]);
    const rootTreePath = `${fileTreeRootPath(root)}/`;
    const entries: FileEntry[] = [
      {
        rootId: root.id,
        path: "Series/Season 1/clip.mp4",
        name: "clip.mp4",
        kind: "file",
        sizeBytes: 10,
        mimeType: "video/mp4",
        modifiedAt: null,
      },
    ];

    upsertFileTreeEntries(registry, rootTreePath, entries);
    const meta = registry.get(fileTreeEntryPath(rootTreePath, entries[0]));

    expect(meta).toMatchObject({
      rootId: root.id,
      path: "Series/Season 1/clip.mp4",
      name: "clip.mp4",
      kind: "file",
    });
  });

  it("preserves excluded metadata for tree rendering", () => {
    const registry = createFileTreeRegistry([root]);
    const rootTreePath = `${fileTreeRootPath(root)}/`;
    const [treePath] = upsertFileTreeEntries(registry, rootTreePath, [
      { ...entry("Skipped", "Skipped", "directory"), excluded: true },
    ]);

    expect(registry.get(treePath)?.excluded).toBe(true);
  });

  it("returns expanded unloaded directories once", () => {
    const registry = createFileTreeRegistry([root]);
    const rootTreePath = `${fileTreeRootPath(root)}/`;
    const paths = upsertFileTreeEntries(registry, rootTreePath, [
      {
        rootId: root.id,
        path: "Series",
        name: "Series",
        kind: "directory",
        sizeBytes: null,
        mimeType: null,
        modifiedAt: null,
      },
    ]);
    const isExpanded = vi.fn((treePath: string) => treePath.endsWith("Series/"));

    expect(unloadedExpandedDirectories([rootTreePath, ...paths], registry, new Set([`${root.id}:`]), isExpanded))
      .toEqual([registry.get(`${rootTreePath}Series/`)]);
    expect(unloadedExpandedDirectories([rootTreePath, ...paths], registry, new Set([`${root.id}:`, `${root.id}:Series`]), isExpanded))
      .toEqual([]);
  });

  it("prunes loaded child subtrees that disappear from refreshed backend entries", () => {
    const registry = createFileTreeRegistry([root]);
    const rootTreePath = `${fileTreeRootPath(root)}/`;
    const [videosTreePath] = upsertFileTreeEntries(registry, rootTreePath, [
      entry("Videos", "Videos", "directory"),
    ]);
    upsertFileTreeEntries(registry, rootTreePath, [
      entry("Videos/Friendship (2025)", "Friendship (2025)", "directory"),
      entry("Videos/bbb_sunflower_2160p_60fps.mp4", "bbb_sunflower_2160p_60fps.mp4", "file"),
    ]);
    upsertFileTreeEntries(registry, rootTreePath, [
      entry("Videos/Friendship (2025)/folder.jpg", "folder.jpg", "file"),
    ]);
    const beforePaths = [
      rootTreePath,
      videosTreePath,
      `${rootTreePath}Videos/Friendship (2025)/`,
      `${rootTreePath}Videos/Friendship (2025)/folder.jpg`,
      `${rootTreePath}Videos/bbb_sunflower_2160p_60fps.mp4`,
    ];

    const result = reconcileFileTreeEntries(
      registry,
      beforePaths,
      new Set([`${root.id}:`, `${root.id}:Videos`, `${root.id}:Videos/Friendship (2025)`]),
      rootTreePath,
      registry.get(videosTreePath)!,
      [entry("Videos/bbb_sunflower_2160p_60fps.mp4", "bbb_sunflower_2160p_60fps.mp4", "file")],
    );

    expect(result.treePaths).toContain(`${rootTreePath}Videos/bbb_sunflower_2160p_60fps.mp4`);
    expect(result.treePaths).not.toContain(`${rootTreePath}Videos/Friendship (2025)/`);
    expect(result.treePaths).not.toContain(`${rootTreePath}Videos/Friendship (2025)/folder.jpg`);
    expect(result.loadedKeys.has(`${root.id}:Videos/Friendship (2025)`)).toBe(false);
  });
});

function entry(path: string, name: string, kind: "directory" | "file"): FileEntry {
  return {
    rootId: "root-alpha-1234",
    path,
    name,
    kind,
    sizeBytes: null,
    mimeType: null,
    modifiedAt: null,
  };
}
