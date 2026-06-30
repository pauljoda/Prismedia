<script lang="ts">
  import type { Snippet } from "svelte";
  import ReviewSection from "$lib/components/review/ReviewSection.svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  interface Props {
    /** Section heading icon (rendered in the collapsible header). */
    icon: Snippet;
    title: string;
    panelId: string;
    /** Cards to render in the selectable grid. */
    cards: EntityThumbnailCard[];
    /** Currently-selected card ids (controlled by the parent). */
    selectedIds: string[];
    /** Card ids eligible for selection — Select all targets exactly these. */
    selectableIds: string[];
    onToggle: (id: string, selected: boolean) => void;
    /** Select all eligible cards, or clear the selection. */
    onToggleAll: (selectAll: boolean) => void;
    /** Body click on a card — used to open an info preview without affecting selection. */
    onActivate?: (card: EntityThumbnailCard) => void;
  }

  let { icon, title, panelId, cards, selectedIds, selectableIds, onToggle, onToggleAll, onActivate }: Props = $props();

  const selectedSet = $derived(new Set(selectedIds));
  const allSelected = $derived(selectableIds.length > 0 && selectableIds.every((id) => selectedSet.has(id)));
</script>

<ReviewSection {panelId} {title} meta={`${selectedIds.length}/${cards.length}`} lazy>
  {#snippet icon()}{@render icon()}{/snippet}
  {#snippet actions()}
    <button
      type="button"
      class="text-[0.72rem] font-medium text-text-muted transition-colors hover:text-text-primary"
      onclick={() => onToggleAll(!allSelected)}
    >
      {allSelected ? "Deselect all" : "Select all"}
    </button>
  {/snippet}

  <div class="selectable-card-grid p-3.5">
    {#each cards as card (card.entity.id)}
      <EntityThumbnail
        {card}
        linkable={false}
        selectable
        selectMode
        selected={selectedSet.has(card.entity.id)}
        onSelectedChange={(selected) => onToggle(card.entity.id, selected)}
        onActivate={() => onActivate?.(card)}
      />
    {/each}
  </div>
</ReviewSection>

<style>
  .selectable-card-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(7.5rem, 100%), 1fr));
    gap: 0.75rem;
  }
</style>
