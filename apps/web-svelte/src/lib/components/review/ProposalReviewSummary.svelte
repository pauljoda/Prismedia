<script lang="ts">
  import { Layers, Users } from "@lucide/svelte";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import { relationshipProposals, structuralChildProposals } from "$lib/components/identify-review";
  import { proposalImageUrl, proposalTitle } from "$lib/components/identify/identify-review-helpers";
  import { aspectRatioForKind } from "$lib/entities/entity-thumbnail";
  import ProposalContextBar from "./ProposalContextBar.svelte";
  import ProposalFieldReviewSection from "./ProposalFieldReviewSection.svelte";
  import ProposalNodeGrid from "./ProposalNodeGrid.svelte";
  import ReviewSection from "./ReviewSection.svelte";

  interface Props {
    proposal: EntityMetadataProposal;
    selectedIds?: string[];
    selectableIds?: string[];
    onSelectedChange?: (proposalId: string, selected: boolean) => void;
    onActivate?: ((proposal: EntityMetadataProposal) => void) | null;
    childrenTitle?: string;
    subtitle?: string | null;
  }

  let {
    proposal,
    selectedIds = [],
    selectableIds = [],
    onSelectedChange = () => undefined,
    onActivate = null,
    childrenTitle = "Items",
    subtitle = null,
  }: Props = $props();

  const children = $derived(structuralChildProposals(proposal));
  const relationships = $derived(relationshipProposals(proposal));
  const title = $derived(proposalTitle(proposal));
  const posterUrl = $derived(proposalImageUrl(proposal, ["poster", "cover", "thumbnail", "backdrop"]));
  const imageShape = $derived(aspectRatioForKind(proposal.targetKind));
  const selectedCount = $derived(children.filter((child) => selectedIds.includes(child.proposalId)).length);
  const selectableCount = $derived(children.filter((child) => selectableIds.includes(child.proposalId)).length);
</script>

<div class="flex flex-col gap-4">
  <ProposalContextBar
    {proposal}
    {title}
    {subtitle}
    kindLabel={proposal.targetKind}
    {posterUrl}
    imageShape={imageShape === "square" ? "square" : imageShape === "wide" ? "wide" : "portrait"}
    showReason
  />

  <ProposalFieldReviewSection {proposal} title="Metadata" variant="summary" selectable={false} />

  {#if children.length > 0}
    <ReviewSection
      panelId={`review-children-${proposal.proposalId}`}
      title={childrenTitle}
      meta={selectableCount > 0 ? `${selectedCount} of ${selectableCount} selected` : `${children.length} items`}
    >
      {#snippet icon()}<Layers class="h-3.5 w-3.5 text-text-accent" />{/snippet}
      <div class="p-3.5">
        <ProposalNodeGrid
          nodes={children}
          {selectedIds}
          {selectableIds}
          {onSelectedChange}
          {onActivate}
          selectionMode={selectableCount > 0}
        />
      </div>
    </ReviewSection>
  {/if}

  {#if relationships.length > 0}
    <ReviewSection
      panelId={`review-relationships-${proposal.proposalId}`}
      title="Related metadata"
      meta={`${relationships.length} items`}
      lazy
    >
      {#snippet icon()}<Users class="h-3.5 w-3.5 text-text-accent" />{/snippet}
      <div class="p-3.5">
        <ProposalNodeGrid
          nodes={relationships}
          selectedIds={[]}
          selectableIds={[]}
          onSelectedChange={() => undefined}
          {onActivate}
          selectionMode={false}
        />
      </div>
    </ReviewSection>
  {/if}
</div>
