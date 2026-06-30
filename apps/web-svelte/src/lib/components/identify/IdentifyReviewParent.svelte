<script lang="ts">
  import {
    Check,
    ChevronDown,
    ChevronUp,
    FolderPlus,
    Images,
    Info,
    Layers,
    Loader2,
    Tag,
    Users,
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import ReviewSection from "$lib/components/review/ReviewSection.svelte";
  import IdentifyTargetPreview from "./IdentifyTargetPreview.svelte";
  import IdentifyChildrenGrid from "./IdentifyChildrenGrid.svelte";
  import IdentifyNewContainersGrid from "./IdentifyNewContainersGrid.svelte";
  import IdentifyRejectQueueActions from "./IdentifyRejectQueueActions.svelte";
  import {
    buildRootReviewApplyPayload,
    currentFieldValueForReview,
    defaultImageSelectionForReview,
    defaultFieldSelectionForReview,
    groupReviewImages,
    isNewRelationshipTitle,
    proposalFieldValue,
    proposalHasField,
    reviewImagePreviewUrl,
    structuralChildEntities,
    structuralChildProposals,
    structuralDescendantProposals,
    newStructuralContainerProposals,
    adoptedLocalChildIds,
    entityKindLabel,
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
    tagRelationshipForTitle,
  } from "./identify-review-helpers";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import type { EntityCard, EntityDetailCard } from "$lib/api/entities";
  import { aspectRatioForKind } from "$lib/entities/entity-thumbnail";
  import { useIdentifyStore } from "./identify-store.svelte";

  interface Props {
    entity: EntityCard;
    proposal: EntityMetadataProposal;
    detail?: EntityDetailCard | null;
  }

  let { entity, proposal, detail = null }: Props = $props();

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
  // De-duplicate tags: the tag chips key their `{#each}` on the tag string, so a
  // provider repeating a tag would crash rendering with `each_key_duplicate`.
  const tags = $derived([...new Set(proposal.patch?.tags ?? [])]);
  const existingTagTitles = $derived(relationshipTitlesForDetail(detail, "tag"));
  const looseTags = $derived(tags.filter((tag) => !tagRelationshipForTitle(tag, relationships)));
  const imageGroups = $derived(groupReviewImages(proposal));
  const selectedTagCount = $derived(Object.values(selectedTags).filter(Boolean).length);
  const selectedRelationshipCount = $derived(
    relationships.filter((relationship) => store.isReviewProposalSelected(relationship.proposalId)).length,
  );
  const selectedChildCount = $derived(
    children.filter((child) => store.isReviewProposalSelected(child.proposalId)).length,
  );
  const localChildEntities = $derived(structuralChildEntities(entity.kind, detail?.childrenByKind));
  const cascadeRunning = $derived(store.cascadeRunning(entity.id));
  // New containers the provider proposes (volumes, seasons, discs, …) get their own section, and
  // a child the proposal files inside one moves out of the flat children list — so the review
  // shows each child migrating into its new home as the cascade resolves it.
  const newContainers = $derived(newStructuralContainerProposals(proposal));
  const adoptedIds = $derived(adoptedLocalChildIds(proposal));
  const remainingChildEntities = $derived(localChildEntities.filter((child) => !adoptedIds.has(child.id)));
  const newContainersTitle = $derived(`New ${entityKindLabel(newContainers[0]?.targetKind ?? "")}`);
  const newContainersMeta = $derived.by(() => {
    const filed = `${adoptedIds.size} of ${localChildEntities.length} filed inside`;
    return cascadeRunning ? `${filed} · identifying…` : filed;
  });
  const matchedRemainingCount = $derived.by(() => {
    const bound = new Set(
      structuralDescendantProposals(proposal)
        .map((node) => node.targetEntityId)
        .filter((id): id is string => Boolean(id)),
    );
    return remainingChildEntities.filter((child) => bound.has(child.id)).length;
  });
  const childrenMeta = $derived(
    cascadeRunning
      ? "identifying…"
      : `${matchedRemainingCount} of ${remainingChildEntities.length} matched`,
  );
  const nextQueueItem = $derived(store.nextQueueItem(entity.id));
  const queueIndex = $derived(store.queue.findIndex((item) => item.entityId === entity.id));
  const prevQueueNavItem = $derived(queueIndex > 0 ? store.queue[queueIndex - 1] : null);
  const nextQueueNavItem = $derived(queueIndex >= 0 && queueIndex < store.queue.length - 1 ? store.queue[queueIndex + 1] : null);
  const contextTitle = $derived(proposal.patch?.title?.trim() || entity.title);
  const showEntitySubtitle = $derived(
    entity.title.trim().localeCompare(contextTitle, undefined, { sensitivity: "accent" }) !== 0,
  );
  const contextPosterUrl = $derived(
    selectedProposalImageUrl(proposal, ["poster", "thumbnail", "cover"], selectedImages, proposal.proposalId, store)
    ?? entity.coverUrl
    ?? proposalImageUrl(proposal, ["poster", "thumbnail", "cover"]),
  );
  // Direct videos carry a wide thumbnail/still rather than a portrait poster — show the context
  // chip in the matching orientation.
  const contextImageWide = $derived.by(() => {
    const images = proposal.images ?? [];
    const hasPoster = images.some((image) => image.kind === "poster" || image.kind === "cover");
    const hasWide = images.some((image) => image.kind === "thumbnail" || image.kind === "still" || image.kind === "backdrop");
    return !hasPoster && hasWide;
  });
  // Music artists, albums, and tracks use a square cover rather than a portrait poster.
  const coverIsSquare = $derived(aspectRatioForKind(proposal.targetKind) === "square");
  const applyProgressPercent = $derived.by(() => {
    const progress = store.applyProgress;
    if (!progress) return 0;
    return Math.max(0, Math.min(100, Math.round((progress.currentIndex / Math.max(progress.total, 1)) * 100)));
  });
  const applyProgressPath = $derived((store.applyProgress?.currentPath ?? []).filter((part) => part.trim().length > 0));

  $effect(() => {
    if (reviewStateProposalId === proposal.proposalId) return;
    reviewStateProposalId = proposal.proposalId;
    store.beginProposalReview(proposal);
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

  // The child tree is resolved by a background cascade that streams onto the queue item. Poll it
  // while the cascade runs so children fill in; the poll stops itself when the cascade completes.
  $effect(() => {
    if (!cascadeRunning) return;
    // The shared queue poll streams cascade children onto the open review and stops
    // itself once nothing is in flight, so there is no per-view cleanup to do.
    store.ensureQueuePolling();
  });

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

  function handleApply(navigateNext = false) {
    store.setReviewFieldSelections(proposal.proposalId, selectedFields);
    store.setReviewImageSelections(proposal.proposalId, selectedImages);
    store.setReviewTagSelections(proposal.proposalId, selectedTags);
    const payload = buildRootReviewApplyPayload(proposal, {
      selectedFields,
      selectedImages,
      selectedTags,
      selectedCascade: store.reviewCascadeSelections,
      selectedFieldsByProposal: store.reviewFieldSelections,
      selectedImagesByProposal: store.reviewImageSelections,
      selectedTagsByProposal: store.reviewTagSelections,
    });
    void store.applyProposal(entity, payload.proposal, payload.selectedFields, payload.selectedImages, { navigateNext });
  }

  function walkChild(child: EntityMetadataProposal) {
    store.navigateTo({
      kind: "review-child",
      entity,
      proposal: child,
      parentProposal: proposal,
      ancestors: [proposal],
    });
  }
</script>

<div class="flex flex-col gap-4">
  <!-- Preview of what we are identifying (collapsed by default) -->
  <IdentifyTargetPreview {entity} />

  <!-- Context bar -->
  <div class="grid grid-cols-[auto_1fr_auto_auto_auto] items-center gap-4 rounded-sm border border-border-subtle bg-surface-1 p-3.5 shadow-well">
    {#if contextPosterUrl}
      <img
        src={contextPosterUrl}
        alt=""
        class={cn("rounded-xs object-cover", coverIsSquare ? "h-14 w-14" : contextImageWide ? "h-12 w-[5.5rem]" : "h-16 w-11")}
        decoding="async"
        referrerpolicy="no-referrer"
      />
    {:else}
      <div class={cn("grid place-items-center rounded-xs bg-surface-3", coverIsSquare ? "h-14 w-14" : contextImageWide ? "h-12 w-[5.5rem]" : "h-16 w-11")}>
        <Layers class="h-5 w-5 text-text-disabled" />
      </div>
    {/if}
    <div class="min-w-0">
      <h2 class="truncate">{contextTitle}</h2>
      <div class="mt-1 flex min-w-0 flex-wrap items-center gap-1.5">
        <span class="rounded-xs border border-phosphor-600/20 bg-surface-3 px-1.5 py-0.5 font-mono text-[0.6rem] leading-none text-phosphor-600">
          {entity.kind}
        </span>
        {#if showEntitySubtitle}
          <span class="min-w-0 truncate font-mono text-[0.7rem] text-text-muted">{entity.title}</span>
        {/if}
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
    <div class="hidden flex-col items-end gap-0.5 md:flex">
      <span class="text-kicker">Reason</span>
      <span class="text-[0.74rem] text-text-secondary">{proposal.matchReason ?? "—"}</span>
    </div>
  </div>

  <!-- Base fields -->
  <ReviewSection
    panelId={`base-fields-${proposal.proposalId}`}
    title="Base fields"
    meta={`${DIFF_FIELD_KEYS.filter((k) => selectedFields[k]).length} of ${DIFF_FIELD_KEYS.filter((k) => proposalHasField(proposal, k)).length} accepted`}
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

    <!-- Diff header -->
    <div class="hidden grid-cols-[auto_110px_1fr_1fr] items-center gap-3 border-b border-border-default bg-surface-2 px-3.5 py-1.5 md:grid">
      <span class="w-5"></span>
      <span class="text-kicker">Field</span>
      <span class="text-kicker">Current</span>
      <span class="text-kicker text-text-accent">Proposed</span>
    </div>

    {#each DIFF_FIELD_KEYS as field (field)}
      {#if proposalHasField(proposal, field)}
        {@const current = currentFieldValueForReview(entity, detail, field)}
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
            <div class="mt-1 text-[0.82rem] leading-snug text-text-primary md:mt-0">
              {proposalFieldValue(proposal, field)}
            </div>
          </div>
        </div>
      {/if}
    {/each}
  </ReviewSection>

  <!-- Credits -->
  {#if credits.length > 0}
    <ReviewSection
      panelId={`credits-${proposal.proposalId}`}
      title="Credits"
      meta={`${credits.filter((credit) => store.isReviewProposalSelected(credit.proposalId)).length} of ${credits.length} selected`}
      lazy
    >
      {#snippet icon()}
        <Users class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
      <div class="identify-thumbnail-grid p-3.5">
        {#each credits as credit (credit.proposalId)}
          <EntityThumbnail
            card={creditCard(credit, proposal, relationshipTitlesForDetail(detail, credit.targetKind), selectedImages, proposal.proposalId, store)}
            linkable={false}
            onActivate={() => walkChild(credit)}
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
            card={relationshipCard(relationship, relationshipTitlesForDetail(detail, relationship.targetKind), selectedImages, proposal.proposalId, store)}
            linkable={false}
            onActivate={() => walkChild(relationship)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(relationship.proposalId)}
            onSelectedChange={(selected) => setRelationshipSelected(relationship, selected)}
          />
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
            style="aspect-ratio: {group.kind === 'poster' || group.kind === 'cover'
              ? (coverIsSquare ? '1/1' : '2/3')
              : group.kind === 'backdrop' || group.kind === 'thumbnail' || group.kind === 'still'
                ? '16/9'
                : '2/1'};"
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

  <!-- New structure the provider proposes: children below move in here as they resolve -->
  {#if newContainers.length > 0}
    <ReviewSection
      panelId={`new-containers-${proposal.proposalId}`}
      title={newContainersTitle}
      meta={newContainersMeta}
    >
      {#snippet icon()}
        <FolderPlus class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
      <IdentifyNewContainersGrid containers={newContainers} onWalkChild={walkChild} />
    </ReviewSection>
  {/if}

  <!-- Children: identified one at a time; a child filed into new structure moves up there -->
  {#if remainingChildEntities.length > 0}
    <ReviewSection
      panelId={`children-${proposal.proposalId}`}
      title="Children"
      meta={childrenMeta}
    >
      {#snippet icon()}
        <Layers class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
      <IdentifyChildrenGrid {cascadeRunning} childEntities={remainingChildEntities} {proposal} onWalkChild={walkChild} />
    </ReviewSection>
  {/if}

  {#if store.applying && store.applyProgress}
    <div class="apply-progress-row" aria-live="polite">
      <div class="flex min-w-0 items-center gap-2">
        <span class="grid h-8 w-8 shrink-0 place-items-center rounded-xs border border-border-accent bg-accent-950/40 text-text-accent shadow-[0_0_18px_rgba(242,194,106,0.18)]">
          <Loader2 class="h-4 w-4 animate-spin" />
        </span>
        <div class="min-w-0">
          <div class="flex min-w-0 flex-wrap items-baseline gap-x-2 gap-y-1">
            <span class="font-heading text-[0.82rem] font-semibold text-text-primary">Applying metadata</span>
            <span class="font-mono text-[0.68rem] text-text-muted">
              {store.applyProgress.currentIndex}/{store.applyProgress.total}
            </span>
          </div>
          <div class="mt-1 flex min-w-0 flex-wrap items-center gap-1.5 text-[0.75rem] text-text-secondary">
            {#if applyProgressPath.length > 0}
              {#each applyProgressPath as part, index (index)}
                {#if index > 0}
                  <span class="font-mono text-text-disabled">-&gt;</span>
                {/if}
                <span class="max-w-[14rem] truncate">{part}</span>
              {/each}
            {:else}
              <span>Preparing accepted proposal</span>
            {/if}
          </div>
        </div>
      </div>
      <div class="mt-3 h-1.5 overflow-hidden rounded-xs border border-border-subtle bg-surface-3" role="progressbar" aria-valuenow={applyProgressPercent} aria-valuemin="0" aria-valuemax="100">
        <div class="h-full rounded-xs bg-[linear-gradient(90deg,rgba(213,154,42,0.82),rgba(242,194,106,0.95))] shadow-[0_0_14px_rgba(242,194,106,0.28)] transition-[width] duration-300" style:width={`${applyProgressPercent}%`}></div>
      </div>
    </div>
  {/if}

  <!-- Action footer -->
  <div class="flex flex-col gap-2 py-2 md:flex-row md:items-center md:gap-3">
    {#if store.queue.length > 1 && queueIndex >= 0}
      <div class="flex items-center gap-1.5">
        <button
          type="button"
          class="inline-flex h-8 w-8 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30 md:h-7 md:w-7"
          disabled={!prevQueueNavItem}
          onclick={() => prevQueueNavItem && store.reviewQueueItem(prevQueueNavItem)}
          aria-label="Previous queue item"
        >
          <ChevronUp class="h-3.5 w-3.5" />
        </button>
        <span class="font-mono text-[0.72rem] text-text-muted">
          {queueIndex + 1}/{store.queue.length}
        </span>
        <button
          type="button"
          class="inline-flex h-8 w-8 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30 md:h-7 md:w-7"
          disabled={!nextQueueNavItem}
          onclick={() => nextQueueNavItem && store.reviewQueueItem(nextQueueNavItem)}
          aria-label="Next queue item"
        >
          <ChevronDown class="h-3.5 w-3.5" />
        </button>
      </div>
    {/if}

    <span class="hidden font-mono text-[0.7rem] text-text-muted md:inline">
      {Object.values(selectedFields).filter(Boolean).length} fields
      · {Object.values(selectedImages).filter(Boolean).length} imgs
      · {selectedRelationshipCount} rels
      · {selectedTagCount} tags
      · {selectedChildCount} children
    </span>
    <div class="hidden flex-1 md:block"></div>

    {#if cascadeRunning}
      <span class="font-mono text-[0.7rem] text-text-muted">Identifying children… Accept unlocks when finished</span>
    {/if}

    <!-- Review actions: full-width stacked on mobile -->
    <div class="flex flex-col gap-2 md:flex-row md:items-center md:gap-3" data-testid="identify-proposal-actions">
      <IdentifyRejectQueueActions entityId={entity.id} showNext={Boolean(nextQueueItem)} disabled={store.applying} />
      <div class="flex flex-col gap-2 md:flex-row md:gap-3">
        <button
          type="button"
          class="btn-accent-glow inline-flex h-10 items-center justify-center gap-1.5 rounded-xs border border-border-accent-strong px-3 text-[0.78rem] text-text-primary transition-all disabled:cursor-not-allowed disabled:opacity-40 md:h-9"
          disabled={store.applying || cascadeRunning}
          onclick={() => handleApply(false)}
        >
          {#if store.applying}
            <Loader2 class="h-4 w-4 animate-spin" />
          {:else}
            <Check class="h-4 w-4" />
          {/if}
          Accept
        </button>
        {#if nextQueueItem}
          <button
            type="button"
            class="inline-flex h-10 items-center justify-center gap-1.5 rounded-xs border border-border-accent-strong px-3 text-[0.78rem] text-text-primary transition-all disabled:cursor-not-allowed disabled:opacity-40 md:h-9"
            style="background: linear-gradient(135deg, rgba(242,194,106,0.24), rgba(242,194,106,0.1)); box-shadow: 0 0 18px rgba(242,194,106,0.16);"
            disabled={store.applying || cascadeRunning}
            onclick={() => handleApply(true)}
          >
            {#if store.applying}
              <Loader2 class="h-4 w-4 animate-spin" />
            {:else}
              <Check class="h-4 w-4" />
            {/if}
            Accept and Next
          </button>
        {/if}
      </div>
    </div>
  </div>
</div>

<style>
  .apply-progress-row {
    width: 100%;
    border: 1px solid rgba(242, 194, 106, 0.28);
    border-radius: var(--radius-sm);
    background:
      linear-gradient(135deg, rgba(242, 194, 106, 0.12), rgba(213, 154, 42, 0.05)),
      rgba(20, 20, 22, 0.86);
    box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04), 0 0 26px rgba(242, 194, 106, 0.08);
    padding: 0.875rem;
  }

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

  .identify-artwork-grid[data-artwork-kind="logo"] {
    grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
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

  .btn-accent-glow {
    background: linear-gradient(135deg, var(--color-accent-overlay-light), var(--color-accent-overlay-faint));
    box-shadow: 0 0 18px var(--color-accent-overlay-subtle);
  }

  @media (prefers-reduced-motion: reduce) {
    .identify-artwork-tile::before {
      animation: none;
    }
  }
</style>
