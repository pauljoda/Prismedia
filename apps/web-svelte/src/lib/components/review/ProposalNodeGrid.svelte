<script lang="ts">
  import { Check, Layers } from "@lucide/svelte";
  import { Checkbox, cn } from "@prismedia/ui-svelte";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import { proposalKindToEntityKind } from "$lib/entities/entity-codes";
  import { entityKindIcon } from "$lib/entities/entity-kind-icons";
  import { aspectRatioForKind, toAspectRatioValue } from "$lib/entities/entity-thumbnail";
  import { childMeta, proposalImageUrl, proposalTitle } from "$lib/components/identify/identify-review-helpers";

  interface Props {
    nodes: EntityMetadataProposal[];
    selectedIds: string[];
    selectableIds: string[];
    onSelectedChange: (proposalId: string, selected: boolean) => void;
    onActivate?: ((proposal: EntityMetadataProposal) => void) | null;
    imageUrl?: (proposal: EntityMetadataProposal) => string | null;
    imageAlt?: (proposal: EntityMetadataProposal) => string;
    statusLabel?: (proposal: EntityMetadataProposal) => string | null;
    selectionMode?: boolean;
  }

  let {
    nodes,
    selectedIds,
    selectableIds,
    onSelectedChange,
    onActivate = null,
    imageUrl = (proposal) => proposalImageUrl(proposal, ["poster", "thumbnail", "cover", "logo"]),
    imageAlt = (proposal) => proposalTitle(proposal),
    statusLabel = () => null,
    selectionMode = true,
  }: Props = $props();

  const selected = $derived(new Set(selectedIds));
  const selectable = $derived(new Set(selectableIds));
</script>

<div class="proposal-node-grid">
  {#each nodes as node (node.proposalId)}
    {@const title = proposalTitle(node)}
    {@const entityKind = proposalKindToEntityKind(node.targetKind)}
    {@const NodeIcon = entityKindIcon(entityKind)}
    {@const coverUrl = imageUrl(node)}
    {@const meta = childMeta(node) ?? []}
    {@const canSelect = selectionMode && selectable.has(node.proposalId)}
    {@const isSelected = canSelect && selected.has(node.proposalId)}
    {@const status = statusLabel(node)}
    {#snippet nodeContent()}
      <div class="proposal-node-cover" style={`aspect-ratio: ${toAspectRatioValue(aspectRatioForKind(entityKind))};`}>
        <div class="grid h-full w-full place-items-center">
          <NodeIcon class="h-6 w-6 text-text-disabled" />
        </div>
        {#if coverUrl}
          <img
            src={coverUrl}
            alt={imageAlt(node)}
            loading="lazy"
            decoding="async"
            referrerpolicy="no-referrer"
            class="absolute inset-0 h-full w-full object-cover"
          />
        {/if}
        {#if isSelected}
          <span class="proposal-node-selected" aria-hidden="true"><Check class="h-3 w-3" /></span>
        {/if}
      </div>
      <span class="proposal-node-title">{title}</span>
      <span class="proposal-node-meta">
        {#each meta as item, index (`${item.icon}-${item.label}-${index}`)}
          <span>{item.label}</span>
        {/each}
        {#if status}<span>{status}</span>{/if}
        {#if meta.length === 0 && !status}<span>{node.targetKind}</span>{/if}
      </span>
    {/snippet}
    <article class={cn("proposal-node", isSelected && "is-selected", selectionMode && !canSelect && "is-disabled")}>
      {#if onActivate}
        <button
          type="button"
          class="proposal-node-open"
          aria-label={`Review ${title}`}
          onclick={() => onActivate(node)}
        >
          {@render nodeContent()}
        </button>
      {:else}
        <div class="proposal-node-open">{@render nodeContent()}</div>
      {/if}

      {#if selectionMode}
        <label class="proposal-node-toggle" class:is-disabled={!canSelect}>
          <Checkbox
            checked={isSelected}
            disabled={!canSelect}
            aria-label={`${isSelected ? "Deselect" : "Select"} ${title}`}
            onchange={(event) => canSelect && onSelectedChange(node.proposalId, event.currentTarget.checked)}
          />
          <span>{canSelect ? (isSelected ? "Selected" : "Select") : status ?? "Unavailable"}</span>
        </label>
      {/if}
    </article>
  {/each}
</div>

<style>
  .proposal-node-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(8rem, 100%), 9.5rem));
    justify-content: start;
    gap: 0.5rem;
    content-visibility: auto;
    contain-intrinsic-size: auto 28rem;
  }

  .proposal-node {
    position: relative;
    min-width: 0;
    overflow: hidden;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    background: var(--color-surface-1);
    box-shadow: var(--shadow-well);
    transition: border-color var(--duration-fast), box-shadow var(--duration-fast), transform var(--duration-fast);
  }

  .proposal-node:hover,
  .proposal-node:focus-within {
    border-color: var(--color-border-accent);
    box-shadow: 0 0 18px rgb(242 194 106 / 0.08);
  }

  .proposal-node.is-selected {
    border-color: var(--color-border-accent-strong);
    box-shadow: 0 0 18px rgb(242 194 106 / 0.16);
  }

  .proposal-node.is-disabled {
    opacity: 0.66;
  }

  .proposal-node-open {
    display: flex;
    width: 100%;
    min-width: 0;
    flex-direction: column;
    gap: 0.35rem;
    padding: 0.45rem;
    text-align: left;
  }

  .proposal-node-cover {
    position: relative;
    width: 100%;
    overflow: hidden;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-3);
  }

  .proposal-node-selected {
    position: absolute;
    top: 0.3rem;
    right: 0.3rem;
    display: grid;
    width: 1.25rem;
    height: 1.25rem;
    place-items: center;
    border-radius: var(--radius-xs);
    background: var(--color-accent-500);
    color: var(--color-surface-1);
    box-shadow: var(--shadow-glow-accent);
  }

  .proposal-node-title {
    overflow: hidden;
    color: var(--color-text-primary);
    font-family: var(--font-heading);
    font-size: 0.76rem;
    font-weight: 600;
    line-height: 1.2;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .proposal-node-meta {
    display: flex;
    min-height: 1rem;
    flex-wrap: wrap;
    gap: 0.25rem;
    color: var(--color-text-muted);
    font-family: var(--font-mono);
    font-size: 0.6rem;
  }

  .proposal-node-toggle {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    border-top: 1px solid var(--color-border-subtle);
    padding: 0.4rem 0.5rem;
    color: var(--color-text-muted);
    cursor: pointer;
    font-family: var(--font-mono);
    font-size: 0.62rem;
  }

  .proposal-node-toggle.is-disabled {
    cursor: default;
  }
</style>
