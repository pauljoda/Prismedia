<script lang="ts">
  import { ShieldAlert, Trash2, X } from "@lucide/svelte";
  import { Checkbox, cn, dur, ease } from "@prismedia/ui-svelte";
  import { fly } from "svelte/transition";

  interface ExtraAction {
    id: string;
    label: string;
    variant?: "default" | "danger";
    onRun: () => void | Promise<void>;
  }

  interface Props {
    selectedCount: number;
    visibleCount: number;
    allSelected: boolean;
    itemLabel: string;
    busy?: boolean;
    canMarkNsfw?: boolean;
    canDelete?: boolean;
    actions?: ExtraAction[];
    onSelectAll: () => void;
    onClear: () => void;
    onMarkNsfw?: () => void | Promise<void>;
    onDelete?: () => void | Promise<void>;
  }

  let {
    selectedCount,
    visibleCount,
    allSelected,
    itemLabel,
    busy = false,
    canMarkNsfw = true,
    canDelete = true,
    actions = [],
    onSelectAll,
    onClear,
    onMarkNsfw,
    onDelete,
  }: Props = $props();
</script>

<div
  class={cn(
    "surface-card no-lift flex flex-wrap items-center gap-2 border border-border-subtle px-3 py-2 transition-[border-color,box-shadow] duration-moderate",
    selectedCount > 0 && "border-border-accent shadow-[var(--shadow-glow-accent)]",
  )}
>
  <label class="flex items-center gap-2 text-[0.72rem] text-text-muted">
    <Checkbox
      checked={allSelected}
      disabled={busy || visibleCount === 0}
      onchange={() => onSelectAll()}
    />
    <span>
      {selectedCount > 0
        ? `${selectedCount} selected`
        : `Select ${visibleCount.toLocaleString()} ${itemLabel}`}
    </span>
  </label>

  <div class="flex-1"></div>

  {#if selectedCount > 0}
    <div
      class="flex flex-wrap items-center gap-2"
      in:fly={{ y: 8, duration: dur.moderate, easing: ease.enter }}
      out:fly={{ y: 8, duration: dur.normal, easing: ease.exit }}
    >
      {#if canMarkNsfw && onMarkNsfw}
        <button
          type="button"
          class="inline-flex items-center gap-1.5 border border-border-subtle px-2.5 py-1 text-[0.68rem] text-text-muted transition-colors hover:border-border-accent hover:text-text-primary disabled:opacity-50"
          disabled={busy}
          onclick={() => void onMarkNsfw()}
        >
          <ShieldAlert class="h-3.5 w-3.5 text-text-accent" />
          Mark NSFW
        </button>
      {/if}
      {#each actions as action (action.id)}
        <button
          type="button"
          class={cn(
            "inline-flex items-center gap-1.5 border px-2.5 py-1 text-[0.68rem] transition-colors disabled:opacity-50",
            action.variant === "danger"
              ? "border-status-error/30 text-status-error-text hover:bg-status-error/10"
              : "border-border-subtle text-text-muted hover:border-border-accent hover:text-text-primary",
          )}
          disabled={busy}
          onclick={() => void action.onRun()}
        >
          {action.label}
        </button>
      {/each}
      {#if canDelete && onDelete}
        <button
          type="button"
          class="inline-flex items-center gap-1.5 border border-status-error/30 px-2.5 py-1 text-[0.68rem] text-status-error-text transition-colors hover:bg-status-error/10 disabled:opacity-50"
          disabled={busy}
          onclick={() => void onDelete()}
        >
          <Trash2 class="h-3.5 w-3.5" />
          Delete
        </button>
      {/if}
      <button
        type="button"
        class="inline-flex items-center gap-1.5 border border-border-subtle px-2.5 py-1 text-[0.68rem] text-text-muted transition-colors hover:text-text-primary disabled:opacity-50"
        disabled={busy}
        onclick={onClear}
      >
        <X class="h-3.5 w-3.5" />
        Clear
      </button>
    </div>
  {/if}
</div>
