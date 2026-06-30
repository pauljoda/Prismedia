<script lang="ts">
  import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
  import { proposalKindToEntityKind } from "$lib/entities/entity-codes";
  import {
    Check,
    ChevronLeft,
    ChevronDown,
    ChevronUp,
    Images,
    Info,
    Layers,
    Tag,
    Users,
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import ReviewSection from "$lib/components/review/ReviewSection.svelte";
  import IdentifyTargetPreview from "./IdentifyTargetPreview.svelte";
  import {
    currentFieldValueForReview,
    defaultImageSelectionForReview,
    defaultFieldSelectionForReview,
    groupReviewImages,
    isNewRelationshipTitle,
    proposalFieldValue,
    proposalHasField,
    reviewImagePreviewUrl,
    structuralChildProposals,
    relationshipProposals,
    relationshipTitlesForDetail,
    reviewDiffFieldKeys,
    reviewFieldLabels,
  } from "$lib/components/identify-review";
  import {
    proposalImageUrl,
    selectedProposalImageUrl,
    proposalTitle,
    relationshipCard,
    creditCard,
    childCard as buildChildCard,
    isLocalUnmatchedProposal,
    tagRelationshipForTitle,
  } from "./identify-review-helpers";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import type { EntityCard } from "$lib/api/entities";
  import { aspectRatioForKind } from "$lib/entities/entity-thumbnail";
  import { useIdentifyStore } from "./identify-store.svelte";

  interface Props {
    entity: EntityCard;
    proposal: EntityMetadataProposal;
    parentProposal: EntityMetadataProposal;
    ancestors?: EntityMetadataProposal[];
  }

  let { entity, proposal, parentProposal, ancestors = [parentProposal] }: Props = $props();

  const store = useIdentifyStore();

  const DIFF_FIELD_KEYS = reviewDiffFieldKeys;
  const FIELD_LABELS = reviewFieldLabels;

  let selectedFields = $state<Record<string, boolean>>({});
  let selectedImages = $state<Record<string, string | null>>({});
  let selectedTags = $state<Record<string, boolean>>({});
  let reviewStateProposalId = $state<string | null>(null);

  const children = $derived(structuralChildProposals(proposal));
  const relationships = $derived(relationshipProposals(proposal));
  const credits = $derived(relationships.filter((r) => r.targetKind === "person"));
  const nonCreditRelationships = $derived(relationships.filter((r) => r.targetKind !== "person"));
  // De-duplicate tags so repeated provider tags can't crash the keyed `{#each}`.
  const tags = $derived([...new Set(proposal.patch?.tags ?? [])]);
  const currentScopeEntityId = $derived(parentProposal.targetEntityId ?? entity.id);
  const currentDetail = $derived(store.getReviewDetailForProposal(currentScopeEntityId, proposal));
  const currentDetailEntityId = $derived(store.reviewDetailEntityIdForProposal(currentScopeEntityId, proposal));
  const existingTagTitles = $derived(relationshipTitlesForDetail(currentDetail, "tag"));
  const looseTags = $derived(tags.filter((tag) => !tagRelationshipForTitle(tag, relationships)));
  const imageGroups = $derived(groupReviewImages(proposal));
  const localChildrenById = $derived.by(() => {
    const pairs = (currentDetail?.childrenByKind ?? [])
      .flatMap((group) => group.entities)
      .map((child) => [child.id, child] as const);
    return new Map(pairs);
  });
  const selectedTagCount = $derived(Object.values(selectedTags).filter(Boolean).length);
  const selectedChildCount = $derived(
    children.filter((child) => store.isReviewProposalSelected(child.proposalId)).length,
  );
  const contextTitle = $derived(proposal.patch?.title?.trim() || "Child");
  // Music artists/albums/tracks use a square cover rather than a portrait poster.
  const coverIsSquare = $derived(aspectRatioForKind(proposal.targetKind) === "square");
  const contextPosterUrl = $derived(
    selectedProposalImageUrl(proposal, ["poster", "thumbnail", "cover", "logo"], selectedImages, proposal.proposalId, store)
    ?? proposalImageUrl(proposal, ["poster", "thumbnail", "cover", "logo"]),
  );

  const parentChildren = $derived(structuralChildProposals(parentProposal));
  const currentIndex = $derived(parentChildren.findIndex((c) => c.proposalId === proposal.proposalId));
  const prevChild = $derived(currentIndex > 0 ? parentChildren[currentIndex - 1] : null);
  const nextChild = $derived(currentIndex < parentChildren.length - 1 ? parentChildren[currentIndex + 1] : null);

  $effect(() => {
    void store.ensureReviewDetailForProposal(currentScopeEntityId, proposal);
  });

  $effect(() => {
    if (reviewStateProposalId === proposal.proposalId) return;
    reviewStateProposalId = proposal.proposalId;
    selectedFields = store.getReviewFieldSelections(proposal.proposalId) ??
      defaultFieldSelectionForReview(proposal);
    selectedImages = store.getReviewImageSelections(proposal.proposalId) ??
      defaultImageSelectionForReview(proposal);
    selectedTags = store.getReviewTagSelections(proposal.proposalId) ??
      Object.fromEntries((proposal.patch?.tags ?? []).map((tag) => [tag, true]));
    store.setReviewFieldSelections(proposal.proposalId, selectedFields);
    store.setReviewImageSelections(proposal.proposalId, selectedImages);
    store.setReviewTagSelections(proposal.proposalId, selectedTags);
  });

  function currentEntityFallback(): EntityCard {
    return {
      id: currentDetailEntityId ?? proposal.targetEntityId ?? proposal.proposalId,
      kind: proposalKindToEntityKind(proposal.targetKind),
      title: currentDetail?.title ?? "",
      parentEntityId: null,
      sortOrder: null,
      coverUrl: null,
      coverThumbUrl: null,
      hoverKind: THUMBNAIL_HOVER_KIND.none,
      hoverUrl: null,
      hoverImages: [],
      meta: [],
      rating: null,
      isFavorite: false,
      isNsfw: false,
      isOrganized: false,
    };
  }

  function setRelationshipSelected(result: EntityMetadataProposal, selected: boolean) {
    store.setReviewProposalSelected(result.proposalId, selected);
    if (result.targetKind === "tag") {
      setTagSelected(proposalTitle(result), selected);
    }
  }

  function setTagSelected(tag: string, selected: boolean) {
    selectedTags = { ...selectedTags, [tag]: selected };
    store.setReviewTagSelected(proposal.proposalId, tag, selected);
    const relationship = tagRelationshipForTitle(tag, relationships);
    if (relationship) store.setReviewProposalSelected(relationship.proposalId, selected);
  }

  function setFieldSelected(field: string, selected: boolean) {
    selectedFields = { ...selectedFields, [field]: selected };
    store.setReviewFieldSelected(proposal.proposalId, field, selected);
  }

  function setAllFields(selected: boolean) {
    selectedFields = {
      ...selectedFields,
      ...Object.fromEntries(DIFF_FIELD_KEYS.map((k) => [k, selected ? proposalHasField(proposal, k) : false])),
    };
    store.setReviewFieldSelections(proposal.proposalId, selectedFields);
  }

  function setImageSelected(kind: string, url: string | null) {
    selectedImages = { ...selectedImages, [kind]: url };
    store.setReviewImageSelected(proposal.proposalId, kind, url);
  }

  function goBackToParent() {
    if (ancestors.length <= 1) {
      // Reopen the parent with its live proposal so children resolved while drilled in still show.
      store.navigateTo({ kind: "review-parent", entity, proposal: store.liveProposalFor(entity.id) ?? parentProposal });
      return;
    }
    const nextAncestors = ancestors.slice(0, -1);
    const grandParent = nextAncestors[nextAncestors.length - 1];
    store.navigateTo({ kind: "review-child", entity, proposal: parentProposal, parentProposal: grandParent, ancestors: nextAncestors });
  }

  function goToSibling(sibling: EntityMetadataProposal) {
    store.navigateTo({ kind: "review-child", entity, proposal: sibling, parentProposal, ancestors });
  }

  function goToChild(child: EntityMetadataProposal) {
    store.navigateTo({ kind: "review-child", entity, proposal: child, parentProposal: proposal, ancestors: [...ancestors, proposal] });
  }
</script>

<div class="flex flex-col gap-4">
  <IdentifyTargetPreview {entity} />

  <!-- Nav -->
  <div class="flex flex-col gap-2 md:flex-row md:items-center md:gap-3">
    <button
      type="button"
      class="inline-flex h-10 items-center justify-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary md:h-8 md:flex-none"
      onclick={goBackToParent}
    >
      <ChevronLeft class="h-3.5 w-3.5" />
      {parentProposal.patch?.title ?? entity.title}
    </button>
    <div class="hidden flex-1 md:block"></div>
    <!-- Sibling nav -->
    {#if parentChildren.length > 1}
      <div class="flex items-center justify-center gap-1.5">
        <button
          type="button"
          class="inline-flex h-8 w-8 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30 md:h-7 md:w-7"
          disabled={!prevChild}
          onclick={() => prevChild && goToSibling(prevChild)}
          aria-label="Previous sibling"
        >
          <ChevronUp class="h-3.5 w-3.5" />
        </button>
        <span class="font-mono text-[0.72rem] text-text-muted">
          {currentIndex + 1}/{parentChildren.length}
        </span>
        <button
          type="button"
          class="inline-flex h-8 w-8 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30 md:h-7 md:w-7"
          disabled={!nextChild}
          onclick={() => nextChild && goToSibling(nextChild)}
          aria-label="Next sibling"
        >
          <ChevronDown class="h-3.5 w-3.5" />
        </button>
      </div>
    {/if}
  </div>

  <!-- Context bar -->
  <div class="grid grid-cols-[auto_1fr_auto_auto] items-center gap-4 rounded-sm border border-border-subtle bg-surface-1 p-3.5 shadow-well">
    {#if contextPosterUrl}
      <img src={contextPosterUrl} alt="" class={cn("rounded-xs object-cover", coverIsSquare ? "h-14 w-14" : "h-16 w-11")} decoding="async" referrerpolicy="no-referrer" />
    {:else}
      <div class={cn("grid place-items-center rounded-xs bg-surface-3", coverIsSquare ? "h-14 w-14" : "h-16 w-11")}>
        <Layers class="h-5 w-5 text-text-disabled" />
      </div>
    {/if}
    <div class="min-w-0">
      <h2 class="truncate">{contextTitle}</h2>
      <div class="mt-1 flex min-w-0 flex-wrap items-center gap-1.5">
        <span class="rounded-xs border border-phosphor-600/20 bg-surface-3 px-1.5 py-0.5 font-mono text-[0.6rem] leading-none text-phosphor-600">
          {proposal.targetKind}
        </span>
      </div>
    </div>
    <div class="hidden flex-col items-end gap-0.5 md:flex">
      <span class="text-kicker">Match</span>
      <span class="font-mono font-semibold text-text-accent">
        {proposal.confidence ? `${Math.round(proposal.confidence * 100)}%` : "—"}
      </span>
    </div>
    <div class="hidden flex-col items-end gap-0.5 md:flex">
      <span class="text-kicker">Provider</span>
      <span class="text-[0.82rem] text-text-primary">{proposal.provider}</span>
    </div>
  </div>

  <!-- Base fields -->
  <ReviewSection
    panelId={`base-fields-${proposal.proposalId}`}
    title={proposal.patch?.title ? `Base fields · ${proposal.patch.title}` : "Base fields"}
    meta={`${DIFF_FIELD_KEYS.filter((k) => selectedFields[k]).length}/${DIFF_FIELD_KEYS.filter((k) => proposalHasField(proposal, k)).length} accepted`}
  >
    {#snippet icon()}
      <Info class="h-3.5 w-3.5 text-text-accent" />
    {/snippet}
    {#snippet actions()}
      <button
        type="button"
        class="text-[0.72rem] text-text-muted transition-colors hover:text-text-primary"
        onclick={() => setAllFields(true)}
      >
        All
      </button>
      <button
        type="button"
        class="text-[0.72rem] text-text-muted transition-colors hover:text-text-primary"
        onclick={() => setAllFields(false)}
      >
        None
      </button>
    {/snippet}

    <div class="hidden grid-cols-[auto_110px_1fr_1fr] items-center gap-3 border-b border-border-default bg-surface-2 px-3.5 py-1.5 md:grid">
      <span class="w-5"></span>
      <span class="text-kicker">Field</span>
      <span class="text-kicker">Current</span>
      <span class="text-kicker text-text-accent">Proposed</span>
    </div>

    {#each DIFF_FIELD_KEYS as field (field)}
      {#if proposalHasField(proposal, field)}
        {@const current = currentFieldValueForReview(currentEntityFallback(), currentDetail, field)}
        <div class="grid grid-cols-[auto_minmax(0,1fr)] items-start gap-3 border-b border-border-subtle px-3.5 py-3 last:border-b-0 md:grid-cols-[auto_110px_1fr_1fr]">
          <label class="flex items-center">
            <input
              type="checkbox"
              class="h-4 w-4 accent-accent-500"
              checked={selectedFields[field]}
              onchange={(event) => setFieldSelected(field, event.currentTarget.checked)}
            />
          </label>
          <div class="md:contents">
            <div>
              <span class="font-heading text-[0.76rem] font-semibold text-text-secondary">{FIELD_LABELS[field]}</span>
              <span class="ml-2 font-mono text-[0.62rem] text-text-disabled md:ml-0 md:block">{field}</span>
            </div>
            <div class="hidden text-[0.76rem] leading-snug text-text-muted md:block">{current || "—"}</div>
            <div class="mt-1 text-[0.82rem] leading-snug text-text-primary md:mt-0">{proposalFieldValue(proposal, field)}</div>
          </div>
        </div>
      {/if}
    {/each}
  </ReviewSection>

  <!-- Credits (inherited) -->
  {#if credits.length > 0}
    <ReviewSection
      panelId={`credits-${proposal.proposalId}`}
      title="Credits"
      meta={`inherited · ${credits.filter((credit) => store.isReviewProposalSelected(credit.proposalId)).length} of ${credits.length} selected`}
      lazy
    >
      {#snippet icon()}
        <Users class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
      <div class="identify-thumbnail-grid p-3.5">
        {#each credits as credit (credit.proposalId)}
          <EntityThumbnail
            card={creditCard(credit, proposal, relationshipTitlesForDetail(currentDetail, credit.targetKind), selectedImages, proposal.proposalId, store)}
            linkable={false}
            onActivate={() => goToChild(credit)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(credit.proposalId)}
            onSelectedChange={(selected) => setRelationshipSelected(credit, selected)}
          />
        {/each}
      </div>
    </ReviewSection>
  {/if}

  <!-- Relationships -->
  {#if nonCreditRelationships.length > 0}
    <ReviewSection
      panelId={`relationships-${proposal.proposalId}`}
      title="Relationships"
      meta={`${nonCreditRelationships.filter((relationship) => store.isReviewProposalSelected(relationship.proposalId)).length} of ${nonCreditRelationships.length} selected`}
      lazy
    >
      {#snippet icon()}
        <Layers class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
      <div class="identify-thumbnail-grid p-3.5">
        {#each nonCreditRelationships as relationship (relationship.proposalId)}
          <EntityThumbnail
            card={relationshipCard(relationship, relationshipTitlesForDetail(currentDetail, relationship.targetKind), selectedImages, proposal.proposalId, store)}
            linkable={false}
            onActivate={() => goToChild(relationship)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(relationship.proposalId)}
            onSelectedChange={(selected) => setRelationshipSelected(relationship, selected)}
          />
        {/each}
      </div>
    </ReviewSection>
  {/if}

  <!-- Tags -->
  {#if looseTags.length > 0}
    <ReviewSection
      panelId={`tags-${proposal.proposalId}`}
      title="Tags"
      meta={`${selectedTagCount} of ${tags.length} selected`}
    >
      {#snippet icon()}
        <Tag class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
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
            aria-pressed={selectedTags[tag]}
            onclick={() => setTagSelected(tag, !selectedTags[tag])}
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

  <!-- Artwork — one card per kind -->
  {#each imageGroups as group (group.kind)}
    <ReviewSection
      panelId={`artwork-${group.kind}-${proposal.proposalId}`}
      title={group.kind.charAt(0).toUpperCase() + group.kind.slice(1)}
      meta={`${group.images.length} candidate${group.images.length === 1 ? "" : "s"}${selectedImages[group.kind] ? " · 1 selected" : ""}`}
      lazy
    >
      {#snippet icon()}
        <Images class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
      <div
        class="identify-artwork-grid p-3.5"
        data-artwork-kind={group.kind}
      >
        {#each group.images as image (image.url)}
          <button
            type="button"
            class={cn(
              "identify-artwork-tile relative overflow-hidden rounded-xs border bg-surface-3 transition-all",
              selectedImages[group.kind] === image.url
                ? "border-border-accent-strong shadow-[0_0_16px_rgba(242,194,106,0.2)]"
                : "border-border-default hover:border-border-accent",
            )}
            style="aspect-ratio: {group.kind === 'poster' || group.kind === 'cover' ? (coverIsSquare ? '1/1' : '2/3') : group.kind === 'backdrop' ? '16/9' : '2/1'};"
            onclick={() => setImageSelected(group.kind, selectedImages[group.kind] === image.url ? null : image.url)}
          >
            <img
              src={reviewImagePreviewUrl(image, proposal.targetKind)}
              alt=""
              class="h-full w-full object-cover"
              loading="lazy"
              decoding="async"
              referrerpolicy="no-referrer"
              fetchpriority="low"
              onload={(e) => e.currentTarget.closest('.identify-artwork-tile')?.classList.add('is-loaded')}
            />
            {#if selectedImages[group.kind] === image.url}
              <div class="absolute right-1 top-1">
                <span class="grid h-4 w-4 place-items-center rounded-xs bg-accent-500 text-[#0b0b0c]">
                  <Check class="h-2.5 w-2.5" />
                </span>
              </div>
            {/if}
            <div class="absolute bottom-0 left-0 right-0 flex justify-between bg-black/75 px-1.5 py-1">
              <span class="font-mono text-[0.58rem] text-phosphor-600">{image.source}</span>
              {#if image.width && image.height}
                <span class="font-mono text-[0.58rem] text-text-disabled">{image.width}×{image.height}</span>
              {/if}
            </div>
          </button>
        {/each}
      </div>
    </ReviewSection>
  {/each}

  <!-- Children (episodes) -->
  {#if children.length > 0}
    <ReviewSection
      panelId={`children-${proposal.proposalId}`}
      title="Children"
      meta={`${selectedChildCount} of ${children.length} selected`}
      lazy
    >
      {#snippet icon()}
        <Layers class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
      <div class="identify-thumbnail-grid p-3.5">
        {#each children as child, i (child.proposalId)}
          {@const localUnmatched = isLocalUnmatchedProposal(child)}
          <EntityThumbnail
            card={buildChildCard(child, i, "Episode", "video", selectedImages, proposal.proposalId, store, child.targetEntityId ? localChildrenById.get(child.targetEntityId) : null)}
            linkable={false}
            onActivate={() => goToChild(child)}
            selectable={!localUnmatched}
            selectMode
            selected={!localUnmatched && store.isReviewProposalSelected(child.proposalId)}
            onSelectedChange={(selected) => !localUnmatched && store.setReviewProposalSelected(child.proposalId, selected)}
          />
        {/each}
      </div>
    </ReviewSection>
  {/if}

  <!-- Action footer -->
  <div class="flex flex-col gap-2 py-2 md:flex-row md:items-center md:gap-3">
    <button
      type="button"
      class="inline-flex h-10 items-center justify-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-3 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary md:h-9 md:flex-none"
      onclick={goBackToParent}
    >
      <ChevronLeft class="h-3.5 w-3.5" />
      {parentProposal.patch?.title ?? entity.title}
    </button>
    {#if parentChildren.length > 1}
      <div class="flex items-center justify-center gap-1.5">
        <button
          type="button"
          class="inline-flex h-8 w-8 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30 md:h-7 md:w-7"
          disabled={!prevChild}
          onclick={() => prevChild && goToSibling(prevChild)}
          aria-label="Previous sibling"
        >
          <ChevronUp class="h-3.5 w-3.5" />
        </button>
        <span class="font-mono text-[0.72rem] text-text-muted">
          {currentIndex + 1}/{parentChildren.length}
        </span>
        <button
          type="button"
          class="inline-flex h-8 w-8 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30 md:h-7 md:w-7"
          disabled={!nextChild}
          onclick={() => nextChild && goToSibling(nextChild)}
          aria-label="Next sibling"
        >
          <ChevronDown class="h-3.5 w-3.5" />
        </button>
      </div>
    {/if}
  </div>
</div>

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

  .identify-artwork-grid[data-artwork-kind="backdrop"] {
    grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr));
  }

  .identify-artwork-grid[data-artwork-kind="logo"] {
    grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
  }

  @media (min-width: 768px) {
    .identify-artwork-grid[data-artwork-kind="poster"] {
      grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
    }

    .identify-artwork-grid[data-artwork-kind="backdrop"] {
      grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
    }
  }

  .identify-artwork-tile::before {
    position: absolute;
    inset: 0;
    z-index: 0;
    content: "";
    pointer-events: none;
    background:
      linear-gradient(110deg, transparent 0%, rgb(242 194 106 / 0.12) 42%, transparent 68%),
      radial-gradient(circle at 50% 45%, rgb(255 255 255 / 0.07), transparent 36%),
      linear-gradient(135deg, rgb(13 14 16), rgb(27 24 19));
    background-size: 220% 100%, auto, auto;
    animation: identify-artwork-shimmer 1.2s ease-in-out infinite;
  }

  .identify-artwork-tile.is-loaded::before {
    opacity: 0;
    animation: none;
  }

  .identify-artwork-tile img {
    position: relative;
    z-index: 1;
  }

  .identify-artwork-tile > div {
    z-index: 2;
  }

  @keyframes identify-artwork-shimmer {
    from { background-position: 180% 0, 0 0, 0 0; }
    to { background-position: -80% 0, 0 0, 0 0; }
  }

  @media (prefers-reduced-motion: reduce) {
    .identify-artwork-tile::before {
      animation: none;
    }
  }
</style>
