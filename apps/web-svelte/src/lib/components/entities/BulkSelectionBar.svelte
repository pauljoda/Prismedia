<script lang="ts">
  import { BellOff, CheckCheck, EllipsisVertical, Flame, ListChecks, X } from "@lucide/svelte";
  import { cubicOut } from "svelte/easing";
  import { slide } from "svelte/transition";
  import { cn } from "@prismedia/ui-svelte";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import type { CollectionEntityType } from "$lib/collections/models";
  import type { EntityGridBulkAction } from "$lib/entities/entity-grid";
  import AddToCollectionMenu from "./AddToCollectionMenu.svelte";

  interface Props {
    allSelectedNsfw?: boolean;
    /** True when every selected card is a wanted placeholder; enables the Remove wanted action. */
    allSelectedWanted?: boolean;
    /** Removes the selected wanted placeholders (delete + discovery blacklist; a direct re-request restores). */
    onRemoveWanted?: () => void;
    bulkActions?: EntityGridBulkAction[];
    class?: string;
    collectionItems?: { entityType: CollectionEntityType; entityId: string }[];
    onClearSelection: () => void;
    onSelectAllVisible: () => void;
    onSelectionActiveChange?: (active: boolean) => void;
    onToggleNsfwFlag?: (markNsfw: boolean) => void;
    selectedCount: number;
    selectedIds: string[];
    selectionActive?: boolean;
    showNsfwAction?: boolean;
    showSelectionToggle?: boolean;
    tuckedAfterPrevious?: boolean;
    variant?: "toolbar" | "track-list";
  }

  let {
    allSelectedNsfw = false,
    allSelectedWanted = false,
    onRemoveWanted,
    bulkActions = [],
    class: className = "",
    collectionItems = [],
    onClearSelection,
    onSelectAllVisible,
    onSelectionActiveChange,
    onToggleNsfwFlag,
    selectedCount,
    selectedIds,
    selectionActive = true,
    showNsfwAction = true,
    showSelectionToggle = true,
    tuckedAfterPrevious = false,
    variant = "toolbar",
  }: Props = $props();

  let actionsMenuOpen = $state(false);

  const canToggleNsfw = $derived(showNsfwAction && typeof onToggleNsfwFlag === "function");
  const canRemoveWanted = $derived(allSelectedWanted && typeof onRemoveWanted === "function");
</script>

<div
  class={cn(
    "bulk-bar",
    variant === "toolbar" && "toolbar-bar",
    variant === "track-list" && "track-list-bar",
    tuckedAfterPrevious && "is-tucked-after-previous",
    className,
  )}
  role="status"
  aria-live="polite"
  transition:slide={{ duration: 200, easing: cubicOut }}
>
  {#if showSelectionToggle}
    <button
      type="button"
      class="bulk-btn select-toggle"
      class:is-active={selectionActive}
      aria-pressed={selectionActive}
      title={selectionActive ? "Exit selection" : "Select items"}
      onclick={() => onSelectionActiveChange?.(!selectionActive)}
    >
      {#if selectionActive}
        <X class="h-3.5 w-3.5" />
        <span class="bulk-btn-label">Done</span>
      {:else}
        <ListChecks class="h-3.5 w-3.5" />
        <span class="bulk-btn-label">Select</span>
      {/if}
    </button>
  {/if}

  {#if selectionActive || !showSelectionToggle}
    <span class="bulk-count">{selectedCount} selected</span>

    <div class="bulk-controls">
      <button
        type="button"
        class="bulk-btn"
        title="Select all visible"
        onclick={onSelectAllVisible}
      >
        <CheckCheck class="h-3.5 w-3.5" />
        <span class="bulk-btn-label">Select all</span>
      </button>
      <button
        type="button"
        class="bulk-btn"
        title="Clear selection"
        disabled={selectedCount === 0}
        onclick={onClearSelection}
      >
        <X class="h-3.5 w-3.5" />
        <span class="bulk-btn-label">Clear</span>
      </button>

      {#if selectedCount > 0}
        {#if canToggleNsfw}
          <span class="bulk-divider" aria-hidden="true"></span>
          <button
            type="button"
            class="bulk-btn"
            title={allSelectedNsfw ? "Mark SFW" : "Mark NSFW"}
            onclick={() => onToggleNsfwFlag?.(!allSelectedNsfw)}
          >
            <Flame class="h-3.5 w-3.5" />
            <span class="bulk-btn-label">{allSelectedNsfw ? "Mark SFW" : "Mark NSFW"}</span>
          </button>
        {/if}

        {#if canRemoveWanted}
          <span class="bulk-divider" aria-hidden="true"></span>
          <button
            type="button"
            class="bulk-btn is-danger"
            title="Remove from Wanted — deletes these placeholders and keeps them out of future discovery; requesting one again brings it back"
            onclick={() => onRemoveWanted?.()}
          >
            <BellOff class="h-3.5 w-3.5" />
            <span class="bulk-btn-label">Remove wanted</span>
          </button>
        {/if}

        {#if collectionItems.length > 0}
          <AddToCollectionMenu items={collectionItems} />
        {/if}

        {#if bulkActions.length > 0}
          <span class="bulk-divider" aria-hidden="true"></span>
          <div class="bulk-actions-menu">
            <button
              type="button"
              class="bulk-btn"
              class:is-active={actionsMenuOpen}
              title="Actions"
              aria-label="Bulk actions"
              aria-expanded={actionsMenuOpen}
              onclick={() => (actionsMenuOpen = !actionsMenuOpen)}
            >
              <EllipsisVertical class="h-3.5 w-3.5" />
              <span class="bulk-btn-label">Actions</span>
            </button>
            {#if actionsMenuOpen}
              <button
                type="button"
                class="fixed inset-0 z-40 cursor-default"
                aria-label="Close actions menu"
                onclick={() => (actionsMenuOpen = false)}
              ></button>
              <div class="bulk-flyout" use:keepFlyoutOnScreen>
                {#each bulkActions as action (action.id)}
                  <button
                    type="button"
                    class="bulk-flyout-item"
                    class:danger={action.tone === "danger"}
                    onclick={() => {
                      action.onRun(selectedIds);
                      actionsMenuOpen = false;
                    }}
                  >
                    {action.label}
                  </button>
                {/each}
              </div>
            {/if}
          </div>
        {/if}
      {/if}
    </div>
  {/if}
</div>

<style>
  .bulk-bar {
    --toolbar-detail-border: var(--color-border, #1c2235);
    --toolbar-detail-glass: rgb(12 15 21);
    --toolbar-detail-slideout-inset: 5px;
    --toolbar-bar-overlap: 0.5rem;

    position: relative;
    z-index: 1;
    display: flex;
    align-items: center;
    gap: 0.75rem;
    min-width: 0;
    min-height: 2.55rem;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    pointer-events: auto;
  }

  .toolbar-bar {
    margin-inline: var(--toolbar-detail-slideout-inset);
    margin-top: calc(-1 * var(--toolbar-bar-overlap));
    border: 1px solid var(--toolbar-detail-border);
    border-top: 0;
    border-radius: 0 0 var(--radius-md, 10px) var(--radius-md, 10px);
    background: var(--toolbar-detail-glass);
    padding: calc(0.6rem + var(--toolbar-bar-overlap)) 0.85rem 0.6rem;
  }

  .toolbar-bar.is-tucked-after-previous {
    z-index: 0;
    margin-top: calc(-1 * var(--toolbar-bar-overlap));
    padding-top: calc(0.6rem + var(--toolbar-bar-overlap));
  }

  .track-list-bar {
    border-top: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgb(255 255 255 / 0.02);
    padding: 0.65rem 0.75rem;
  }

  .bulk-count {
    color: var(--color-text-accent);
    text-transform: uppercase;
    flex-shrink: 0;
  }

  .bulk-controls {
    display: flex;
    align-items: center;
    gap: 0.35rem;
    margin-left: auto;
    min-width: 0;
    flex-wrap: wrap;
    justify-content: flex-end;
  }

  .bulk-divider {
    display: inline-block;
    width: 1px;
    height: 1.1rem;
    background: linear-gradient(
      to bottom,
      transparent,
      rgb(255 255 255 / 0.08),
      transparent
    );
    margin: 0 0.1rem;
  }

  .bulk-btn {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    height: 1.85rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    letter-spacing: 0.04em;
    padding: 0 0.55rem;
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    transition:
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .bulk-btn:hover {
    border-color: var(--color-border-accent, rgba(196, 154, 90, 0.25));
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .bulk-btn:focus-visible {
    outline: none;
    border-color: var(--color-border-accent, rgba(196, 154, 90, 0.25));
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .bulk-btn.is-active {
    border-color: var(--color-border-accent, rgba(196, 154, 90, 0.25));
    background: var(--color-surface-4, #1c2235);
    color: var(--color-text-accent, #f2c26a);
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .bulk-btn:disabled {
    cursor: not-allowed;
    opacity: 0.5;
  }

  .bulk-btn.is-danger:hover,
  .bulk-btn.is-danger:focus-visible {
    border-color: rgba(255, 92, 67, 0.42);
    color: var(--color-status-error-text, #ff806f);
    box-shadow: 0 0 0 1px rgba(255, 92, 67, 0.3), 0 0 8px rgba(255, 92, 67, 0.12);
  }

  .bulk-btn-label {
    display: none;
  }

  @media (min-width: 520px) {
    .bulk-btn-label {
      display: inline;
    }
  }

  .bulk-actions-menu {
    position: relative;
  }

  .bulk-flyout {
    position: absolute;
    right: 0;
    top: calc(100% + 0.3rem);
    z-index: 50;
    min-width: 10rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgb(12, 15, 21);
    border-radius: var(--radius-sm, 6px);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    padding: 0.3rem 0;
    overflow: hidden;
  }

  .bulk-flyout-item {
    display: flex;
    align-items: center;
    gap: 0.55rem;
    width: 100%;
    padding: 0.45rem 0.85rem;
    background: transparent;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    letter-spacing: 0.04em;
    text-align: left;
    transition:
      background-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .bulk-flyout-item:hover {
    background: rgb(255 255 255 / 0.04);
    color: var(--color-text-primary);
  }

  .bulk-flyout-item.danger {
    color: var(--color-text-muted);
  }

  .bulk-flyout-item.danger:hover {
    background: rgb(168 72 80 / 0.12);
    color: var(--color-error-text, #cc7880);
  }
</style>
