<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import FileDetailPane from "$lib/components/files/FileDetailPane.svelte";
  import FileTreePane from "$lib/components/files/FileTreePane.svelte";
  import {
    createFileFolder as apiCreateFileFolder,
    deleteFile as apiDeleteFile,
    excludeFile as apiExcludeFile,
    fetchFileChildren,
    fetchFileDetail,
    fetchFileRoots,
    moveFile as apiMoveFile,
    renameFile as apiRenameFile,
    removeFileExclusion as apiRemoveFileExclusion,
    rescanFileRoot as apiRescanFileRoot,
    uploadFiles as apiUploadFiles,
    type FileDetail,
    type FileRoot,
    type FileUploadItem,
  } from "$lib/api/files";
  import { refreshEntity } from "$lib/api/entities";
  import type { FileActionId } from "$lib/files/file-actions";
  import {
    createFileTreeRegistry,
    fileTreeRootPath,
    reconcileFileTreeEntries,
    type FileTreeNodeMeta,
    upsertFileTreeEntries,
  } from "$lib/files/file-tree-state";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  let roots = $state<FileRoot[]>([]);
  let registry = $state(new Map<string, FileTreeNodeMeta>());
  let treePaths = $state<string[]>([]);
  let loadedKeys = $state(new Set<string>());
  let selectedTreePath = $state<string | null>(null);
  let detail = $state<FileDetail | null>(null);
  let treeSearch = $state("");
  let loadingTree = $state(false);
  let loadingDetail = $state(false);
  let error = $state<string | null>(null);
  let mobileDetail = $state(false);
  let treeWidthPx = $state(320);
  let resizing = $state(false);
  let syncedQueryKey = "";
  let lastNsfwMode = $state<string | null>(null);
  const nsfw = useNsfw();

  const selectedMeta = $derived(selectedTreePath ? registry.get(selectedTreePath) ?? null : null);

  function loadedKey(meta: Pick<FileTreeNodeMeta, "rootId" | "path">): string {
    return `${meta.rootId}:${meta.path}`;
  }

  function parentPath(path: string): string {
    return path.split("/").filter(Boolean).slice(0, -1).join("/");
  }

  function basename(path: string): string {
    return path.split("/").filter(Boolean).at(-1) ?? path;
  }

  function directoryTarget(meta: FileTreeNodeMeta | null): FileTreeNodeMeta | null {
    if (!meta) return roots[0] ? registry.get(`${fileTreeRootPath(roots[0], roots)}/`) ?? null : null;
    if (meta.kind === "directory") return meta;
    const parent = parentPath(meta.path);
    return [...registry.values()].find((candidate) => candidate.rootId === meta.rootId && candidate.path === parent) ?? meta;
  }

  async function loadRoots(): Promise<void> {
    loadingTree = true;
    error = null;
    try {
      const response = await fetchFileRoots();
      roots = response.roots;
      const nextRegistry = createFileTreeRegistry(roots);
      const rootTreePaths = roots.map((r) => `${fileTreeRootPath(r, roots)}/`);
      const allPaths = [...rootTreePaths];
      const nextLoadedKeys = new Set<string>();

      const results = await Promise.allSettled(roots.map(async (root) => {
        const rootTreePath = `${fileTreeRootPath(root, roots)}/`;
        const children = await fetchFileChildren(root.id, "");
        return { rootTreePath, root, children };
      }));

      for (const result of results) {
        if (result.status !== "fulfilled") continue;
        const { rootTreePath, root, children } = result.value;
        const childPaths = upsertFileTreeEntries(nextRegistry, rootTreePath, children.entries);
        allPaths.push(...childPaths);
        nextLoadedKeys.add(`${root.id}:`);
      }

      registry = nextRegistry;
      treePaths = [...new Set(allPaths)];
      loadedKeys = nextLoadedKeys;
    } catch (loadError) {
      error = loadError instanceof Error ? loadError.message : "Failed to load watched roots";
    } finally {
      loadingTree = false;
    }
  }

  async function loadChildren(meta: FileTreeNodeMeta): Promise<void> {
    const key = loadedKey(meta);
    if (loadedKeys.has(key)) return;
    loadingTree = true;
    try {
      const response = await fetchFileChildren(meta.rootId, meta.path);
      const rootTreePath = [...registry.values()].find((candidate) => candidate.rootId === meta.rootId && candidate.path === "")?.treePath;
      if (!rootTreePath) return;
      const nextRegistry = new Map(registry);
      const reconciled = reconcileFileTreeEntries(nextRegistry, treePaths, loadedKeys, rootTreePath, meta, response.entries);
      const childPaths = reconciled.childPaths;
      registry = nextRegistry;
      treePaths = reconciled.treePaths;
      loadedKeys = new Set([...reconciled.loadedKeys, key]);
    } catch (loadError) {
      error = loadError instanceof Error ? loadError.message : "Failed to load folder";
    } finally {
      loadingTree = false;
    }
  }

  async function loadDetail(meta: FileTreeNodeMeta): Promise<void> {
    loadingDetail = true;
    error = null;
    try {
      detail = await fetchFileDetail(meta.rootId, meta.path);
    } catch (loadError) {
      detail = null;
      error = loadError instanceof Error ? loadError.message : "Failed to load file details";
    } finally {
      loadingDetail = false;
    }
  }

  async function selectTreePath(treePath: string, options: { replaceUrl?: boolean; showDetail?: boolean } = {}): Promise<void> {
    const meta = registry.get(treePath);
    if (!meta) return;
    selectedTreePath = treePath;

    const params = new URLSearchParams({ rootId: meta.rootId });
    if (meta.path) params.set("path", meta.path);
    await goto(`/files?${params.toString()}`, { replaceState: options.replaceUrl ?? false, noScroll: true, keepFocus: true });

    if (meta.kind === "file" && (options.showDetail ?? true)) mobileDetail = true;
    await loadDetail(meta);
  }

  async function refreshSelected(): Promise<void> {
    if (!selectedMeta) return;
    const target = selectedMeta.kind === "directory" ? selectedMeta : directoryTarget(selectedMeta);
    if (target) {
      const nextLoaded = new Set(loadedKeys);
      nextLoaded.delete(loadedKey(target));
      loadedKeys = nextLoaded;
      await loadChildren(target);
    }
    await loadDetail(selectedMeta);
  }

  async function createFolder(meta: FileTreeNodeMeta): Promise<void> {
    const name = prompt("Folder name");
    if (!name?.trim()) return;
    await apiCreateFileFolder({ rootId: meta.rootId, parentPath: meta.path, name: name.trim() });
    await refreshSelected();
  }

  async function renameFile(meta: FileTreeNodeMeta, proposedName?: string): Promise<void> {
    if (!meta.path) {
      alert("Library roots cannot be renamed here.");
      return;
    }
    const name = proposedName ?? prompt("New name", meta.name);
    if (!name?.trim() || name.trim() === meta.name) return;
    await apiRenameFile({ rootId: meta.rootId, path: meta.path, name: name.trim() });
    const parent = directoryTarget({ ...meta, kind: "file" });
    if (parent) loadedKeys = new Set([...loadedKeys].filter((key) => key !== loadedKey(parent)));
    selectedTreePath = null;
    await loadRoots();
  }

  async function moveFile(meta: FileTreeNodeMeta, targetDirectoryTreePath?: string | null): Promise<void> {
    if (!meta.path) {
      alert("Library roots cannot be moved here.");
      return;
    }
    const targetDirectory = targetDirectoryTreePath ? registry.get(targetDirectoryTreePath) : null;
    const defaultPath = targetDirectory
      ? [targetDirectory.path, basename(meta.path)].filter(Boolean).join("/")
      : meta.path;
    const targetPath = targetDirectory
      ? defaultPath
      : prompt("Target relative path", defaultPath);
    if (!targetPath?.trim() || targetPath.trim() === meta.path) return;
    await apiMoveFile({
      sourceRootId: meta.rootId,
      sourcePath: meta.path,
      targetRootId: targetDirectory?.rootId ?? meta.rootId,
      targetPath: targetPath.trim(),
    });
    selectedTreePath = null;
    await loadRoots();
  }

  async function deleteFile(meta: FileTreeNodeMeta): Promise<void> {
    if (!meta.path) {
      alert("Library roots cannot be deleted here.");
      return;
    }
    const message =
      meta.kind === "directory"
        ? `Permanently delete ${meta.name} and everything inside it?`
        : `Permanently delete ${meta.name}?`;
    if (!confirm(message)) return;
    await apiDeleteFile(meta.rootId, meta.path);
    selectedTreePath = null;
    detail = null;
    await loadRoots();
  }

  async function rescan(meta: FileTreeNodeMeta): Promise<void> {
    await apiRescanFileRoot({ rootId: meta.rootId, path: meta.path || null });
    if (detail?.linkedEntities?.length) {
      await Promise.allSettled(
        detail.linkedEntities.map((linked) => refreshEntity(linked.entityId)),
      );
    }
    await refreshSelected();
  }

  async function excludePath(meta: FileTreeNodeMeta): Promise<void> {
    if (!meta.path) {
      alert("Library roots cannot be excluded here.");
      return;
    }
    await apiExcludeFile({ rootId: meta.rootId, path: meta.path });
    await refreshSelected();
  }

  async function removeExclusion(meta: FileTreeNodeMeta): Promise<void> {
    if (!meta.path) return;
    await apiRemoveFileExclusion({ rootId: meta.rootId, path: meta.path });
    await refreshSelected();
  }

  async function handleAction(action: FileActionId, treePath = selectedTreePath): Promise<void> {
    const meta = treePath ? registry.get(treePath) : null;
    if (!meta) return;
    try {
      if (action === "open") await selectTreePath(meta.treePath);
      if (action === "new-folder") await createFolder(meta.kind === "directory" ? meta : directoryTarget(meta)!);
      if (action === "rename") await renameFile(meta);
      if (action === "move") await moveFile(meta);
      if (action === "delete") await deleteFile(meta);
      if (action === "rescan") await rescan(meta);
      if (action === "exclude") await excludePath(meta);
      if (action === "remove-exclusion") await removeExclusion(meta);
    } catch (operationError) {
      const message = operationError instanceof Error ? operationError.message : "File operation failed";
      error = message.includes("already exists")
        ? `${message} Choose a different name and try again.`
        : message;
    }
  }

  async function uploadItems(items: FileUploadItem[]): Promise<void> {
    const target = directoryTarget(selectedMeta);
    if (!target || items.length === 0) return;
    try {
      await apiUploadFiles(target.rootId, target.path, items);
      const nextLoaded = new Set(loadedKeys);
      nextLoaded.delete(loadedKey(target));
      loadedKeys = nextLoaded;
      await loadChildren(target);
      if (selectedMeta?.kind === "file") await loadDetail(target);
    } catch (uploadError) {
      const message = uploadError instanceof Error ? uploadError.message : "Upload failed";
      error = message.includes("already exists")
        ? `${message} Rename the incoming file or upload into a new folder.`
        : message;
    }
  }

  function uploadFileList(files: FileList | null): void {
    if (!files?.length) return;
    void uploadItems(Array.from(files).map((file) => ({
      file,
      relativePath: (file as File & { webkitRelativePath?: string }).webkitRelativePath || file.name,
    })));
  }

  async function collectDroppedFiles(dataTransfer: DataTransfer | null): Promise<FileUploadItem[]> {
    if (!dataTransfer) return [];
    const items = [...dataTransfer.items].filter((item) => item.kind === "file");
    const collected = await Promise.all(items.map(async (item) => {
      const entry = "webkitGetAsEntry" in item
        ? (item as DataTransferItem & { webkitGetAsEntry: () => FileSystemEntry | null }).webkitGetAsEntry()
        : null;
      if (entry) return traverseEntry(entry, "");
      const file = item.getAsFile();
      return file ? [{ file, relativePath: file.name }] : [];
    }));
    const flattened = collected.flat();
    return flattened.length
      ? flattened
      : Array.from(dataTransfer.files).map((file) => ({ file, relativePath: file.name }));
  }

  interface FileSystemEntry {
    isFile: boolean;
    isDirectory: boolean;
    name: string;
  }

  interface FileSystemFileEntry extends FileSystemEntry {
    file: (success: (file: File) => void, error?: (error: DOMException) => void) => void;
  }

  interface FileSystemDirectoryEntry extends FileSystemEntry {
    createReader: () => {
      readEntries: (success: (entries: FileSystemEntry[]) => void, error?: (error: DOMException) => void) => void;
    };
  }

  async function traverseEntry(entry: FileSystemEntry, basePath: string): Promise<FileUploadItem[]> {
    const relativePath = [basePath, entry.name].filter(Boolean).join("/");
    if (entry.isFile) {
      const file = await new Promise<File>((resolve, reject) => {
        (entry as FileSystemFileEntry).file(resolve, reject);
      });
      return [{ file, relativePath }];
    }
    if (!entry.isDirectory) return [];
    const reader = (entry as FileSystemDirectoryEntry).createReader();
    const entries = await new Promise<FileSystemEntry[]>((resolve, reject) => {
      reader.readEntries(resolve, reject);
    });
    const nested = await Promise.all(entries.map((child) => traverseEntry(child, relativePath)));
    return nested.flat();
  }

  async function handleExternalDrop(dataTransfer: DataTransfer | null): Promise<void> {
    await uploadItems(await collectDroppedFiles(dataTransfer));
  }

  $effect(() => {
    if (roots.length === 0) return;
    const rootId = page.url.searchParams.get("rootId");
    const path = page.url.searchParams.get("path") ?? "";
    const queryKey = `${rootId}:${path}`;
    if (!rootId || queryKey === syncedQueryKey) return;
    syncedQueryKey = queryKey;
    const root = roots.find((candidate) => candidate.id === rootId);
    if (!root) return;
    const rootTreePath = `${fileTreeRootPath(root, roots)}/`;
    void (async () => {
      const rootMeta = registry.get(rootTreePath);
      if (!rootMeta) return;
      if (path && !registry.has(`${rootTreePath}${path}`)) await loadChildren(rootMeta);
      await selectTreePath(path ? `${rootTreePath}${path}` : rootTreePath, { replaceUrl: true, showDetail: false });
    })();
  });

  function onResizeStart(event: PointerEvent): void {
    event.preventDefault();
    resizing = true;
    const startX = event.clientX;
    const startWidth = treeWidthPx;

    function onMove(moveEvent: PointerEvent): void {
      const delta = moveEvent.clientX - startX;
      treeWidthPx = Math.max(200, Math.min(startWidth + delta, window.innerWidth * 0.6));
    }

    function onUp(): void {
      resizing = false;
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
    }

    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
  }

  onMount(() => {
    lastNsfwMode = nsfw.mode;
    void loadRoots();
  });

  $effect(() => {
    if (lastNsfwMode === null || nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    selectedTreePath = null;
    detail = null;
    registry = new Map();
    treePaths = [];
    loadedKeys = new Set();
    syncedQueryKey = "";
    void loadRoots();
  });
</script>

<svelte:head>
  <title>Files · Prismedia</title>
</svelte:head>

<main
  class:show-detail={mobileDetail}
  class:resizing
  class="files-page"
  style:--tree-width="{treeWidthPx}px"
>
  <FileTreePane
    paths={treePaths}
    {registry}
    {loadedKeys}
    selectedPath={selectedTreePath}
    search={treeSearch}
    loading={loadingTree}
    onSearch={(value) => (treeSearch = value)}
    onSelect={(treePath) => void selectTreePath(treePath)}
    onExpand={(meta) => void loadChildren(meta)}
    onMove={(source, target) => void moveFile(registry.get(source)!, target)}
    onRename={(treePath, newName) => void renameFile(registry.get(treePath)!, newName)}
    onAction={(action, treePath) => void handleAction(action, treePath)}
  />

  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="resize-handle" onpointerdown={onResizeStart}>
    <div class="resize-handle-bar"></div>
  </div>

  <FileDetailPane
    {detail}
    loading={loadingDetail}
    {error}
    mobile={mobileDetail}
    onBack={() => (mobileDetail = false)}
    onRefresh={() => void refreshSelected()}
    onAction={(action) => void handleAction(action)}
    onUploadFiles={uploadFileList}
    onUploadFolder={uploadFileList}
    onExternalDrop={(dataTransfer) => void handleExternalDrop(dataTransfer)}
  />
</main>

<style>
  .files-page {
    display: grid;
    width: calc(100% + 2.5rem);
    max-width: calc(100% + 2.5rem);
    min-width: 0;
    min-height: 0;
    overflow: hidden;
    background: var(--color-bg);
    margin: -1.25rem;
    height: calc(100% + 2.5rem);
  }

  .files-page :global(.detail-pane) {
    display: none;
  }

  .files-page.show-detail :global(.files-tree-pane) {
    display: none;
  }

  .files-page.show-detail :global(.detail-pane) {
    display: grid;
  }

  .resize-handle {
    display: none;
  }

  .files-page.resizing {
    user-select: none;
    cursor: col-resize;
  }

  @media (min-width: 768px) {
    .files-page {
      grid-template-columns: var(--tree-width, 320px) auto minmax(0, 1fr);
    }

    .files-page :global(.detail-pane),
    .files-page.show-detail :global(.detail-pane),
    .files-page.show-detail :global(.files-tree-pane) {
      display: grid;
    }

    .resize-handle {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 5px;
      cursor: col-resize;
      background: var(--color-border-subtle);
      transition: background var(--duration-fast) var(--ease-default);
      touch-action: none;
    }

    .resize-handle:hover,
    .resizing .resize-handle {
      background: var(--color-border-accent);
    }

    .resize-handle-bar {
      width: 1px;
      height: 100%;
      background: transparent;
    }
  }
</style>
