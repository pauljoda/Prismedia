<script lang="ts">
  import { BellOff, CheckCheck, EllipsisVertical, Flame, ListChecks, X } from "@lucide/svelte";
  import { cubicOut } from "svelte/easing";
  import { slide } from "svelte/transition";
  import { Button, buttonVariants, cn, DropdownMenu, Separator } from "@prismedia/ui-svelte";
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

  const canToggleNsfw = $derived(showNsfwAction && typeof onToggleNsfwFlag === "function");
  const canRemoveWanted = $derived(allSelectedWanted && typeof onRemoveWanted === "function");
  const availableBulkActions = $derived(
    bulkActions.filter((action) => action.isAvailable?.(selectedIds) ?? true),
  );
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
    <Button
      variant={selectionActive ? "secondary" : "ghost"}
      size="sm"
      aria-label={selectionActive ? "Done" : "Select"}
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
    </Button>
  {/if}

  {#if selectionActive || !showSelectionToggle}
    <span class="bulk-count">{selectedCount} selected</span>

    <div class="bulk-controls">
      <Button
        variant="ghost"
        size="sm"
        aria-label="Select all"
        title="Select all visible"
        onclick={onSelectAllVisible}
      >
        <CheckCheck class="h-3.5 w-3.5" />
        <span class="bulk-btn-label">Select all</span>
      </Button>
      <Button
        variant="ghost"
        size="sm"
        aria-label="Clear"
        title="Clear selection"
        disabled={selectedCount === 0}
        onclick={onClearSelection}
      >
        <X class="h-3.5 w-3.5" />
        <span class="bulk-btn-label">Clear</span>
      </Button>

      {#if selectedCount > 0}
        {#if canToggleNsfw}
          <Separator orientation="vertical" class="mx-0.5 h-4" />
          <Button
            variant="ghost"
            size="sm"
            aria-label={allSelectedNsfw ? "Mark SFW" : "Mark NSFW"}
            title={allSelectedNsfw ? "Mark SFW" : "Mark NSFW"}
            onclick={() => onToggleNsfwFlag?.(!allSelectedNsfw)}
          >
            <Flame class="h-3.5 w-3.5" />
            <span class="bulk-btn-label">{allSelectedNsfw ? "Mark SFW" : "Mark NSFW"}</span>
          </Button>
        {/if}

        {#if canRemoveWanted}
          <Separator orientation="vertical" class="mx-0.5 h-4" />
          <Button
            variant="danger"
            size="sm"
            aria-label="Remove wanted"
            title="Remove from Wanted — deletes these placeholders and keeps them out of future discovery; requesting one again brings it back"
            onclick={() => onRemoveWanted?.()}
          >
            <BellOff class="h-3.5 w-3.5" />
            <span class="bulk-btn-label">Remove wanted</span>
          </Button>
        {/if}

        {#if collectionItems.length > 0}
          <AddToCollectionMenu items={collectionItems} />
        {/if}

        {#if availableBulkActions.length > 0}
          <Separator orientation="vertical" class="mx-0.5 h-4" />
          <DropdownMenu.Root>
            <DropdownMenu.Trigger
              class={buttonVariants({ variant: "secondary", size: "sm" })}
              title="Actions"
              aria-label="Bulk actions"
            >
              <EllipsisVertical class="h-3.5 w-3.5" />
              <span class="bulk-btn-label">Actions</span>
            </DropdownMenu.Trigger>
            <DropdownMenu.Content align="end" aria-label="Bulk actions">
              <DropdownMenu.Group>
                {#each availableBulkActions as action (action.id)}
                  <DropdownMenu.Item
                    variant={action.tone === "danger" ? "destructive" : "default"}
                    onSelect={() => action.onRun(selectedIds)}
                  >
                    {action.label}
                  </DropdownMenu.Item>
                {/each}
              </DropdownMenu.Group>
            </DropdownMenu.Content>
          </DropdownMenu.Root>
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

  .bulk-btn-label {
    display: none;
  }

  @media (min-width: 520px) {
    .bulk-btn-label {
      display: inline;
    }
  }
</style>
