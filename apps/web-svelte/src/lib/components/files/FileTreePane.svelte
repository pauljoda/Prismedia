<script lang="ts">
  import { onMount } from "svelte";
  import type { ContextMenuItem, ContextMenuOpenContext, FileTree, FileTreeDirectoryHandle } from "@pierre/trees";
  import type { FileActionId } from "$lib/files/file-actions";
  import { fileContextActions } from "$lib/files/file-actions";
  import type { FileTreeNodeMeta } from "$lib/files/file-tree-state";
  import { unloadedExpandedDirectories } from "$lib/files/file-tree-state";

  interface Props {
    paths: string[];
    registry: Map<string, FileTreeNodeMeta>;
    loadedKeys: Set<string>;
    selectedPath: string | null;
    search: string;
    loading?: boolean;
    onSearch?: (value: string) => void;
    onSelect?: (treePath: string) => void;
    onExpand?: (meta: FileTreeNodeMeta) => void;
    onMove?: (sourceTreePath: string, targetDirectoryTreePath: string | null) => void;
    onAction?: (action: FileActionId, treePath: string) => void;
    onRename?: (treePath: string, newName: string) => void;
  }

  let {
    paths,
    registry,
    loadedKeys,
    selectedPath,
    search,
    loading = false,
    onSearch,
    onSelect,
    onExpand,
    onMove,
    onAction,
    onRename,
  }: Props = $props();

  let host: HTMLDivElement;
  let tree = $state<FileTree | null>(null);
  let unsubscribe: (() => void) | null = null;
  let removeTreeClickBridge: (() => void) | null = null;
  let suppressSelectionEvent = false;
  let lastPathsKey = "";

  function basename(path: string): string {
    return path.split("/").filter(Boolean).at(-1) ?? path;
  }

  function renderContextMenu(item: ContextMenuItem, context: ContextMenuOpenContext): HTMLElement {
    const menu = document.createElement("div");
    menu.dataset.testid = "files-context-menu";
    menu.dataset.fileTreeContextMenuRoot = "true";
    Object.assign(menu.style, {
      position: "fixed",
      zIndex: "9999",
      display: "grid",
      minWidth: "10rem",
      border: "1px solid rgba(164, 172, 185, 0.12)",
      background: "#181d27",
      boxShadow: "0 12px 40px rgba(0, 0, 0, 0.6)",
      backdropFilter: "blur(20px)",
      fontFamily: "Inter, system-ui, sans-serif",
    });

    const rect = context.anchorRect;
    const meta = registry.get(item.path as string);
    const isRoot = meta?.path === "";
    const actions = fileContextActions(item.kind, isRoot);
    const menuHeight = actions.length * 32 + 2;
    const menuWidth = 160;
    const top = rect.bottom + menuHeight > window.innerHeight ? Math.max(4, rect.top - menuHeight) : rect.bottom;
    const left = rect.right + menuWidth > window.innerWidth ? Math.max(4, rect.right - menuWidth) : rect.left;
    menu.style.top = `${top}px`;
    menu.style.left = `${left}px`;

    for (const action of actions) {
      const button = document.createElement("button");
      button.type = "button";
      button.textContent = action.label;
      button.dataset.action = action.id;
      Object.assign(button.style, {
        display: "block",
        width: "100%",
        border: "0",
        borderRadius: "0",
        background: "transparent",
        color: action.destructive ? "#cc7880" : "#c8ccd4",
        padding: "0.5rem 0.75rem",
        textAlign: "left",
        font: "500 0.8rem Inter, system-ui, sans-serif",
        cursor: "pointer",
      });
      button.addEventListener("mouseenter", () => {
        button.style.background = "#1f2533";
        button.style.color = action.destructive ? "#cc7880" : "#f5f2ea";
      });
      button.addEventListener("mouseleave", () => {
        button.style.background = "transparent";
        button.style.color = action.destructive ? "#cc7880" : "#c8ccd4";
      });
      button.addEventListener("click", () => {
        if (action.id === "rename") {
          tree?.startRenaming(item.path);
        } else {
          onAction?.(action.id, item.path);
        }
        context.close();
      });
      menu.append(button);
    }

    return menu;
  }

  function checkLazyExpansion(): void {
    if (!tree) return;
    const targets = unloadedExpandedDirectories(paths, registry, loadedKeys, (treePath) => {
      const item = tree?.getItem(treePath);
      if (!item || !item.isDirectory()) return false;
      return (item as FileTreeDirectoryHandle).isExpanded();
    });
    for (const meta of targets) onExpand?.(meta);
  }

  function bridgeRowClick(event: MouseEvent): void {
    const row = event
      .composedPath()
      .find((target): target is HTMLElement =>
        target instanceof HTMLElement && target.matches("button[data-type='item']"),
      );
    const treePath = row?.dataset.itemPath;
    if (treePath) onSelect?.(treePath);
  }

  const treeCSS = `
    :host {
      --trees-fg-override: var(--color-text-primary, #f5f2ea);
      --trees-muted-fg-override: var(--color-text-muted, #a4acb9);
      --trees-border-color-override: var(--color-border-subtle, rgba(164, 172, 185, 0.06));
      --trees-selected-bg-override: var(--color-surface-2, #11151c);
      --trees-focus-ring-override: var(--color-border-accent-strong, rgba(199, 155, 92, 0.45));
      background: var(--color-surface-1, #0d1017) !important;
      color: var(--color-text-primary, #f5f2ea);
    }
    [data-file-tree-virtualized-wrapper],
    [data-file-tree-virtualized-root],
    [data-file-tree-virtualized-scroll],
    [data-file-tree-virtualized-list],
    [data-file-tree-virtualized-sticky],
    [data-truncate-marker] {
      background: var(--color-surface-1, #0d1017) !important;
      color: var(--color-text-primary, #f5f2ea);
    }
    input[data-file-tree-search-input] {
      border: 1px solid var(--color-border-default, rgba(164, 172, 185, 0.12));
      border-radius: 0;
      background: var(--color-surface-2, #11151c) !important;
      color: var(--color-text-primary, #f5f2ea);
      font-family: Inter, system-ui, sans-serif;
    }
    input[data-file-tree-search-input]::placeholder {
      color: var(--color-text-disabled, #5a6070);
    }
    button[data-type='item'] {
      border-radius: 0;
      background: transparent !important;
      color: var(--color-text-secondary, #c8ccd4);
      font-family: Inter, system-ui, sans-serif;
      font-size: 0.8rem;
    }
    button[data-type='item']:hover {
      background: var(--color-surface-2, #11151c) !important;
    }
    button[data-type='item'][data-item-selected] {
      background: var(--color-surface-2, #11151c) !important;
      box-shadow: inset 2px 0 0 var(--color-accent-500, #c79b5c);
      color: var(--color-text-primary, #f5f2ea);
    }
    button[data-type='item'][data-item-drag-target='true'] {
      background: rgba(196, 154, 90, 0.10) !important;
      box-shadow: inset 2px 0 0 var(--color-accent-500, #c79b5c);
      color: var(--color-text-primary, #f5f2ea);
    }
    button[data-type='item'][data-item-dragging='true'] {
      opacity: 0.4;
    }
  `;

  function createTree(module: typeof import("@pierre/trees"), initialPaths: string[]): void {
    const rootPaths = initialPaths.filter((p) => registry.get(p)?.path === "");
    tree = new module.FileTree({
      paths: initialPaths,
      initialExpansion: "closed",
      initialExpandedPaths: rootPaths,
      icons: { set: "complete", colored: true },
      search: false,
      dragAndDrop: {
        canDrag: (draggedPaths) => draggedPaths.length === 1,
        canDrop: () => true,
        onDropComplete: (event) => onMove?.(event.draggedPaths[0], event.target.directoryPath),
      },
      renaming: {
        canRename: () => true,
        onRename: (event) => onRename?.(event.sourcePath, basename(event.destinationPath)),
      },
      composition: {
        contextMenu: {
          enabled: true,
          triggerMode: "both",
          buttonVisibility: "always",
          render: renderContextMenu,
        },
      },
      onSelectionChange: (selectedPaths) => {
        if (suppressSelectionEvent) return;
        const next = selectedPaths[0];
        if (next) onSelect?.(next);
      },
      unsafeCSS: treeCSS,
    });
    tree.render({ containerWrapper: host });
    host.addEventListener("click", bridgeRowClick);
    removeTreeClickBridge = () => host.removeEventListener("click", bridgeRowClick);
    unsubscribe = tree.subscribe(checkLazyExpansion);
    checkLazyExpansion();
    lastPathsKey = initialPaths.join("\n");
  }

  onMount(() => {
    let cancelled = false;
    void (async () => {
      const module = await import("@pierre/trees");
      while (paths.length === 0 && !cancelled) {
        await new Promise((r) => setTimeout(r, 50));
      }
      if (cancelled) return;
      createTree(module, paths);
    })();

    return () => {
      cancelled = true;
      removeTreeClickBridge?.();
      unsubscribe?.();
      tree?.cleanUp();
    };
  });

  $effect(() => {
    if (!tree) return;
    const nextKey = paths.join("\n");
    if (nextKey !== lastPathsKey) {
      const expandedPaths = paths.filter((path) => {
        const meta = registry.get(path);
        if (!meta || meta.kind !== "directory") return false;
        if (meta.path === "") return true;
        return loadedKeys.has(`${meta.rootId}:${meta.path}`);
      });
      tree.resetPaths(paths, { initialExpandedPaths: expandedPaths });
      lastPathsKey = nextKey;
      checkLazyExpansion();
    }
  });

  $effect(() => {
    if (!tree) return;
    tree.setSearch(search || null);
  });

  $effect(() => {
    if (!tree || !selectedPath) return;
    const item = tree.getItem(selectedPath);
    if (!item?.isSelected()) {
      suppressSelectionEvent = true;
      item?.select();
      queueMicrotask(() => {
        suppressSelectionEvent = false;
      });
    }
    tree.scrollToPath(selectedPath, { offset: "nearest", focus: false });
  });
</script>

<section class="files-tree-pane" aria-label="Directory tree">
  <div class="tree-toolbar">
    <input
      type="search"
      placeholder="Filter loaded files"
      value={search}
      oninput={(event) => onSearch?.(event.currentTarget.value)}
    />
  </div>
  <div class="tree-host" bind:this={host} aria-busy={loading}></div>
</section>

<style>
  .files-tree-pane {
    display: grid;
    min-height: 0;
    grid-template-rows: auto 1fr;
    border-right: 1px solid var(--color-border-subtle);
    background: var(--color-surface-1);
  }

  .tree-toolbar {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    border-bottom: 1px solid var(--color-border-subtle);
    padding: 0.5rem 0.75rem;
    color: var(--color-text-muted);
  }

  .tree-toolbar input {
    min-width: 0;
    width: 100%;
    border: 1px solid var(--color-border-default);
    border-radius: 0;
    background: var(--color-surface-2);
    color: var(--color-text-primary);
    padding: 0.38rem 0.55rem;
    font-size: 0.8rem;
    outline: none;
  }

  .tree-toolbar input:focus {
    border-color: var(--color-border-accent-strong);
    box-shadow: var(--shadow-focus-accent);
  }

  .tree-host {
    min-height: 0;
    height: 100%;
  }
</style>
