<script lang="ts">
  import { onMount } from "svelte";
  import { FILE_ENTRY_KIND } from "$lib/api/generated/codes";
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
      zIndex: "2147483647",
      display: "grid",
      minWidth: "10rem",
      border: "1px solid var(--color-border-default, rgba(164, 172, 185, 0.12))",
      borderRadius: "var(--radius-sm, 6px)",
      background: "var(--color-surface-2, #11161d)",
      boxShadow: "0 12px 40px rgba(0, 0, 0, 0.6)",
      backdropFilter: "blur(20px)",
      fontFamily: "var(--font-inter, Inter), system-ui, sans-serif",
      overflow: "hidden",
      transform: "translateZ(0)",
    });

    const rect = context.anchorRect;
    const meta = registry.get(item.path as string);
    const isRoot = meta?.path === "";
    const actions = fileContextActions(item.kind, isRoot, meta?.excluded);
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
      const defaultColor = action.destructive
        ? "var(--color-error-text, #ff9f92)"
        : "var(--color-text-secondary, #c8ccd4)";
      const hoverColor = action.destructive
        ? "var(--color-error-text, #ff9f92)"
        : "var(--color-text-primary, #f0ede3)";
      Object.assign(button.style, {
        display: "block",
        width: "100%",
        border: "0",
        borderRadius: "0",
        background: "transparent",
        color: defaultColor,
        padding: "0.5rem 0.75rem",
        textAlign: "left",
        font: "500 0.8rem var(--font-inter, Inter), system-ui, sans-serif",
        cursor: "pointer",
      });
      button.addEventListener("mouseenter", () => {
        button.style.background = "var(--color-surface-3, #202734)";
        button.style.color = hoverColor;
      });
      button.addEventListener("mouseleave", () => {
        button.style.background = "transparent";
        button.style.color = defaultColor;
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

    // @pierre/trees slots the menu into its shadow host, whose virtualized
    // wrapper establishes a stacking/containing context — so even our maxed-out
    // z-index can't lift the menu above a sibling pane's sticky EntityGrid
    // toolbar, and the fixed coordinates resolve against the wrapper instead of
    // the viewport. Reparent to <body> once the library has mounted it: the slot
    // host tracks content by reference and tears it down with element.remove(),
    // so relocating the node leaves its open/close lifecycle intact.
    queueMicrotask(() => {
      if (menu.isConnected) document.body.appendChild(menu);
    });

    return menu;
  }

  function syncExcludedRows(): void {
    if (!host) return;
    for (const row of host.querySelectorAll<HTMLElement>("button[data-type='item']")) {
      const treePath = row.dataset.itemPath;
      row.dataset.fileExcluded = treePath && registry.get(treePath)?.excluded ? "true" : "false";
    }
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
      --trees-fg-override: var(--color-text-primary, #f0ede3);
      --trees-muted-fg-override: var(--color-text-muted, #a4acb9);
      --trees-border-color-override: var(--color-border-subtle, rgba(164, 172, 185, 0.07));
      --trees-selected-bg-override: var(--color-surface-2, #11161d);
      --trees-focus-ring-color-override: var(--color-border-accent-strong, rgba(242, 194, 106, 0.52));
      --trees-selected-focused-border-color-override: var(--color-border-accent, rgba(242, 194, 106, 0.24));
      background: var(--color-surface-1, #0b0e12) !important;
      color: var(--color-text-primary, #f0ede3);
      font-family: var(--font-body, Inter), sans-serif;
    }
    [data-file-tree-virtualized-wrapper],
    [data-file-tree-virtualized-root],
    [data-file-tree-virtualized-scroll],
    [data-file-tree-virtualized-list],
    [data-file-tree-virtualized-sticky],
    [data-truncate-marker] {
      background: var(--color-surface-1, #0b0e12) !important;
      color: var(--color-text-primary, #f0ede3);
    }
    input[data-file-tree-search-input] {
      border: 1px solid var(--color-border-default, rgba(164, 172, 185, 0.12));
      border-radius: var(--radius-xs, 4px);
      background: var(--color-surface-1, #0b0e12) !important;
      color: var(--color-text-primary, #f0ede3);
      font-family: var(--font-body, Inter), sans-serif;
      font-size: 0.85rem;
      box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.25);
      padding: 0.45rem 0.6rem;
    }
    input[data-file-tree-search-input]:focus {
      border-color: var(--color-border-accent-strong, rgba(242, 194, 106, 0.52));
      box-shadow: 0 0 0 2px rgba(242, 194, 106, 0.20);
      outline: none;
    }
    input[data-file-tree-search-input]::placeholder {
      color: var(--color-text-disabled, #5a6070);
    }
    button[data-type='item'] {
      border-radius: var(--radius-xs, 4px);
      background: transparent !important;
      color: var(--color-text-secondary, #c8ccd4);
      font-family: var(--font-body, Inter), sans-serif;
      font-size: 0.8rem;
      transition: background 100ms ease, color 100ms ease;
    }
    button[data-type='item']:hover {
      background: var(--color-surface-2, #11161d) !important;
      color: var(--color-text-primary, #f0ede3);
    }
    *:focus,
    *:focus-visible {
      outline: none !important;
    }
    button[data-type='item'][data-item-selected] {
      background: rgba(242, 194, 106, 0.06) !important;
      box-shadow: inset 0 0 0 1px var(--color-border-accent, rgba(242, 194, 106, 0.24));
      color: var(--color-text-primary, #f0ede3);
    }
    button[data-type='item'][data-item-drag-target='true'] {
      background: rgba(242, 194, 106, 0.08) !important;
      box-shadow: inset 2px 0 0 var(--color-accent-500, #f2c26a);
      color: var(--color-text-primary, #f0ede3);
    }
    button[data-type='item'][data-item-dragging='true'] {
      opacity: 0.4;
    }
    button[data-type='item'][data-file-excluded='true'] {
      color: var(--color-text-disabled, #5a6070);
      opacity: 0.58;
    }
    button[data-type='item'][data-file-excluded='true']:hover,
    button[data-type='item'][data-file-excluded='true'][data-item-selected] {
      color: var(--color-text-muted, #a4acb9);
    }
    /* Context menu trigger button */
    button[data-type='item'] [data-context-menu-trigger] {
      color: var(--color-text-disabled, #5a6070);
      transition: color 100ms ease;
    }
    button[data-type='item']:hover [data-context-menu-trigger],
    button[data-type='item'][data-item-selected] [data-context-menu-trigger] {
      color: var(--color-text-muted, #a4acb9);
    }
  `;

  function createTree(module: typeof import("@pierre/trees"), initialPaths: string[]): void {
    tree = new module.FileTree({
      paths: initialPaths,
      initialExpansion: "closed",
      // Start every folder — roots included — collapsed; the user opens what they want.
      initialExpandedPaths: [],
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
    syncExcludedRows();
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
      // Preserve exactly what the user currently has open across the rebuild. Deriving this from
      // the tree's live expansion (rather than "every directory we've ever loaded") keeps the
      // collapsed-by-default behavior intact — loading a folder's children no longer forces other
      // branches, or the roots, back open.
      const expandedPaths = paths.filter((path) => {
        const meta = registry.get(path);
        if (!meta || meta.kind !== FILE_ENTRY_KIND.directory) return false;
        const item = tree?.getItem(path);
        return Boolean(item?.isDirectory() && (item as FileTreeDirectoryHandle).isExpanded());
      });
      tree.resetPaths(paths, { initialExpandedPaths: expandedPaths });
      lastPathsKey = nextKey;
      checkLazyExpansion();
      queueMicrotask(syncExcludedRows);
    }
  });

  $effect(() => {
    registry;
    paths;
    if (!tree) return;
    queueMicrotask(syncExcludedRows);
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
    border-radius: var(--radius-xs);
    background: var(--color-surface-1);
    color: var(--color-text-primary);
    padding: 0.45rem 0.6rem;
    font-family: var(--font-body);
    font-size: 0.85rem;
    box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.25);
    outline: none;
  }

  .tree-toolbar input:focus {
    border-color: var(--color-border-accent-strong);
    box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.25), 0 0 0 2px rgba(242, 194, 106, 0.20);
  }

  .tree-host {
    min-height: 0;
    height: 100%;
  }

  .tree-host :global(*:focus),
  .tree-host :global(*:focus-visible) {
    outline: none !important;
  }
</style>
