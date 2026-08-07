<script lang="ts">
  import type { Snippet } from "svelte";
  import { Check, Images, Layers, Tag, Users, X } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import type { EntityDetailCard } from "$lib/api/entities";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import {
    groupReviewImages,
    isNewRelationshipTitle,
    relationshipProposals,
    relationshipTitlesForDetail,
    reviewImagePreviewUrl,
  } from "$lib/components/identify-review";
  import {
    creditCard,
    proposalTitle,
    relationshipCard,
    tagRelationshipForTitle,
  } from "$lib/components/identify/identify-review-helpers";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import ProposalContextBar from "./ProposalContextBar.svelte";
  import ProposalFieldReviewSection from "./ProposalFieldReviewSection.svelte";
  import ReviewSection from "./ReviewSection.svelte";

  interface Props {
    proposal: EntityMetadataProposal;
    title: string;
    subtitle?: string | null;
    kindLabel: string;
    posterUrl?: string | null;
    imageShape?: "portrait" | "square" | "wide";
    detail?: EntityDetailCard | null;
    selectedFields: Record<string, boolean>;
    selectedImages: Record<string, string | null>;
    selectedTags: Record<string, boolean>;
    currentValue?: (field: string) => string;
    isProposalSelected: (proposalId: string) => boolean;
    imageSelectionsForProposal?: (proposalId: string) => Record<string, string | null> | null | undefined;
    onFieldChange: (field: string, selected: boolean) => void;
    onAllFields: (selected: boolean) => void;
    onImageChange: (kind: string, url: string | null) => void;
    onTagChange: (tag: string, selected: boolean) => void;
    onProposalSelected: (proposal: EntityMetadataProposal, selected: boolean) => void;
    onActivate?: ((proposal: EntityMetadataProposal) => void) | null;
    structure?: Snippet;
  }

  let {
    proposal,
    title,
    subtitle = null,
    kindLabel,
    posterUrl = null,
    imageShape = "portrait",
    detail = null,
    selectedFields,
    selectedImages,
    selectedTags,
    currentValue = () => "",
    isProposalSelected,
    imageSelectionsForProposal = () => null,
    onFieldChange,
    onAllFields,
    onImageChange,
    onTagChange,
    onProposalSelected,
    onActivate = null,
    structure,
  }: Props = $props();

  const relationships = $derived(relationshipProposals(proposal));
  const credits = $derived(relationships.filter((relationship) => relationship.targetKind === ENTITY_KIND.person));
  const nonCreditRelationships = $derived(
    relationships.filter((relationship) => relationship.targetKind !== ENTITY_KIND.person),
  );
  const tags = $derived([...new Set(proposal.patch?.tags ?? [])]);
  const existingTagTitles = $derived(relationshipTitlesForDetail(detail, ENTITY_KIND.tag));
  const looseTags = $derived(tags.filter((tag) => !tagRelationshipForTitle(tag, relationships)));
  const imageGroups = $derived(groupReviewImages(proposal));
  const selectedTagCount = $derived(tags.filter((tag) => selectedTags[tag]).length);
  const imageSelectionStore = {
    getReviewImageSelections: (proposalId: string) => imageSelectionsForProposal(proposalId),
  };
</script>

<ProposalContextBar
  {proposal}
  {title}
  {subtitle}
  {kindLabel}
  {posterUrl}
  {imageShape}
  showReason
/>

<ProposalFieldReviewSection
  {proposal}
  {selectedFields}
  {currentValue}
  onFieldChange={onFieldChange}
  onAllFields={onAllFields}
/>

{#if credits.length > 0}
  <ReviewSection
    panelId={`credits-${proposal.proposalId}`}
    title="Credits"
    meta={`${credits.filter((credit) => isProposalSelected(credit.proposalId)).length} of ${credits.length} selected`}
    lazy
  >
    {#snippet icon()}<Users class="h-3.5 w-3.5 text-text-accent" />{/snippet}
    <div class="identify-thumbnail-grid p-3.5">
      {#each credits as credit (credit.proposalId)}
        <EntityThumbnail
          card={creditCard(
            credit,
            proposal,
            relationshipTitlesForDetail(detail, credit.targetKind),
            selectedImages,
            proposal.proposalId,
            imageSelectionStore,
          )}
          linkable={false}
          onActivate={onActivate ? () => onActivate?.(credit) : undefined}
          selectable
          selectMode
          selected={isProposalSelected(credit.proposalId)}
          onSelectedChange={(selected) => onProposalSelected(credit, selected)}
        />
      {/each}
    </div>
  </ReviewSection>
{/if}

{#if nonCreditRelationships.length > 0}
  <ReviewSection
    panelId={`relationships-${proposal.proposalId}`}
    title="Relationships"
    meta={`${nonCreditRelationships.filter((relationship) => isProposalSelected(relationship.proposalId)).length} of ${nonCreditRelationships.length} selected`}
    lazy
  >
    {#snippet icon()}<Layers class="h-3.5 w-3.5 text-text-accent" />{/snippet}
    <div class="identify-thumbnail-grid p-3.5">
      {#each nonCreditRelationships as relationship (relationship.proposalId)}
        <EntityThumbnail
          card={relationshipCard(
            relationship,
            relationshipTitlesForDetail(detail, relationship.targetKind),
            selectedImages,
            proposal.proposalId,
            imageSelectionStore,
          )}
          linkable={false}
          onActivate={onActivate ? () => onActivate?.(relationship) : undefined}
          selectable
          selectMode
          selected={isProposalSelected(relationship.proposalId)}
          onSelectedChange={(selected) => onProposalSelected(relationship, selected)}
        />
      {/each}
    </div>
  </ReviewSection>
{/if}

{#each imageGroups as group (group.kind)}
  <ReviewSection
    panelId={`artwork-${group.kind}-${proposal.proposalId}`}
    title={group.kind.charAt(0).toUpperCase() + group.kind.slice(1)}
    meta={`${group.images.length} candidate${group.images.length === 1 ? "" : "s"}${selectedImages[group.kind] ? " · 1 selected" : ""}`}
    lazy
  >
    {#snippet icon()}<Images class="h-3.5 w-3.5 text-text-accent" />{/snippet}
    <div class="identify-artwork-grid p-3.5" data-artwork-kind={group.kind}>
      {#each group.images as image (image.url)}
        <button
          type="button"
          class={cn(
            "identify-artwork-tile relative overflow-hidden rounded-xs border bg-surface-3 transition-all",
            selectedImages[group.kind] === image.url
              ? "border-border-accent-strong shadow-[0_0_16px_rgba(199,201,204,0.2)]"
              : "border-border-default hover:border-border-accent",
          )}
          style="aspect-ratio: {group.kind === 'poster' || group.kind === 'cover'
            ? imageShape === 'square' ? '1/1' : '2/3'
            : group.kind === 'backdrop' || group.kind === 'thumbnail' || group.kind === 'still'
              ? '16/9'
              : '2/1'};"
          aria-label={`${selectedImages[group.kind] === image.url ? "Deselect" : "Select"} ${group.kind} artwork from ${image.source}`}
          aria-pressed={selectedImages[group.kind] === image.url}
          onclick={() => onImageChange(group.kind, selectedImages[group.kind] === image.url ? null : image.url)}
        >
          <img
            src={reviewImagePreviewUrl(image, proposal.targetKind)}
            alt=""
            class="h-full w-full object-cover"
            loading="lazy"
            decoding="async"
            referrerpolicy="no-referrer"
            fetchpriority="low"
            onload={(event) => event.currentTarget.closest(".identify-artwork-tile")?.classList.add("is-loaded")}
          />
          {#if selectedImages[group.kind] === image.url}
            <span class="absolute right-1 top-1 grid h-4 w-4 place-items-center rounded-xs bg-accent-500 text-[#0b0b0c]">
              <Check class="h-2.5 w-2.5" />
            </span>
          {/if}
          <span class="absolute bottom-0 left-0 right-0 flex justify-between bg-black/75 px-1.5 py-1">
            <span class="font-mono text-[0.58rem] text-phosphor-600">{image.source}</span>
            {#if image.width && image.height}
              <span class="font-mono text-[0.58rem] text-text-disabled">{image.width}×{image.height}</span>
            {/if}
          </span>
        </button>
      {/each}
    </div>
  </ReviewSection>
{/each}

{#if looseTags.length > 0}
  <ReviewSection
    panelId={`tags-${proposal.proposalId}`}
    title="Tags"
    meta={`${selectedTagCount} of ${tags.length} selected`}
  >
    {#snippet icon()}<Tag class="h-3.5 w-3.5 text-text-accent" />{/snippet}
    <div class="flex flex-wrap items-center gap-2 p-3.5">
      {#each looseTags as tag (tag)}
        {@const isExisting = !isNewRelationshipTitle(tag, existingTagTitles)}
        <button
          type="button"
          class={cn(
            "inline-flex min-h-8 items-center gap-1.5 rounded-xs border px-2.5 py-1 text-[0.76rem] transition-colors",
            selectedTags[tag]
              ? "border-border-accent bg-accent-950/30 text-text-primary"
              : "border-border-default bg-surface-2 text-text-muted hover:bg-surface-3",
          )}
          aria-label={`${selectedTags[tag] ? "Deselect" : "Select"} tag ${tag}`}
          aria-pressed={selectedTags[tag]}
          onclick={() => onTagChange(tag, !selectedTags[tag])}
        >
          {#if selectedTags[tag]}
            <Check class="h-3 w-3 text-text-accent" />
          {:else}
            <X class="h-3 w-3 text-text-disabled" />
          {/if}
          <span>{tag}</span>
          <span class={cn(
            "rounded-xs border px-1.5 py-0.5 font-mono text-[0.58rem]",
            isExisting
              ? "border-border-default bg-surface-3 text-text-muted"
              : "border-border-accent bg-accent-950/40 text-text-accent",
          )}>
            {isExisting ? "Merge" : "New"}
          </span>
        </button>
      {/each}
    </div>
  </ReviewSection>
{/if}

{@render structure?.()}

<style>
  .identify-thumbnail-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(8rem, 100%), 9.5rem));
    justify-content: start;
    gap: 0.5rem;
    content-visibility: auto;
    contain-intrinsic-size: auto 28rem;
  }

  .identify-artwork-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(8rem, 1fr));
    gap: 0.625rem;
  }

  .identify-artwork-grid[data-artwork-kind="poster"] {
    grid-template-columns: repeat(auto-fill, minmax(9rem, 1fr));
  }

  .identify-artwork-grid[data-artwork-kind="backdrop"],
  .identify-artwork-grid[data-artwork-kind="thumbnail"],
  .identify-artwork-grid[data-artwork-kind="still"] {
    grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr));
  }

  .identify-artwork-tile::before {
    position: absolute;
    inset: 0;
    z-index: 0;
    content: "";
    pointer-events: none;
    background:
      linear-gradient(110deg, transparent 0%, rgb(199 201 204 / 0.12) 42%, transparent 68%),
      radial-gradient(circle at 50% 45%, rgb(255 255 255 / 0.07), transparent 36%),
      linear-gradient(135deg, rgb(13 14 16), rgb(27 24 19));
    background-size: 220% 100%, auto, auto;
    animation: identify-artwork-shimmer 1.2s ease-in-out infinite;
  }

  .identify-artwork-tile.is-loaded::before { opacity: 0; animation: none; }
  .identify-artwork-tile img { position: relative; z-index: 1; }
  .identify-artwork-tile > span { z-index: 2; }

  @keyframes identify-artwork-shimmer {
    from { background-position: 180% 0, 0 0, 0 0; }
    to { background-position: -80% 0, 0 0, 0 0; }
  }

  @media (min-width: 768px) {
    .identify-artwork-grid[data-artwork-kind="poster"] {
      grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
    }

    .identify-artwork-grid[data-artwork-kind="backdrop"],
    .identify-artwork-grid[data-artwork-kind="thumbnail"],
    .identify-artwork-grid[data-artwork-kind="still"] {
      grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .identify-artwork-tile::before { animation: none; }
  }
</style>
