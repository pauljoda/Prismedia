<script lang="ts">
  import {
    Check,
    ChevronLeft,
    ChevronRight,
    Images,
    Info,
    Layers,
    Tag,
    Users,
    X,
  } from "@lucide/svelte";
  import { cn, StatusLed } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import {
    currentFieldValueForReview,
    defaultImageSelectionForReview,
    defaultFieldSelectionForReview,
    groupReviewImages,
    isNewRelationshipTitle,
    proposalFieldValue,
    proposalHasField,
    relationshipTitlesForDetail,
    reviewDiffFieldKeys,
    reviewableImages,
    reviewFieldLabels,
    structuralChildProposals,
    relationshipProposals,
    scopedCreditForProposal,
    reviewImagePreviewUrl,
  } from "$lib/components/identify-review";
  import type {
    CreditPatch,
    EntityMetadataProposal,
    ImageCandidate,
  } from "$lib/api/identify";
  import type { EntityCard } from "$lib/api/prismedia";
  import type { EntityThumbnailCard, EntityThumbnailMetaIcon } from "$lib/entities/entity-thumbnail";
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
  const tags = $derived(proposal.patch?.tags ?? []);
  const currentScopeEntityId = $derived(parentProposal.targetEntityId ?? entity.id);
  const currentDetailEntityId = $derived(store.reviewDetailEntityIdForProposal(currentScopeEntityId, proposal));
  const currentDetail = $derived(store.getReviewDetailForProposal(currentScopeEntityId, proposal));
  const existingTagTitles = $derived(relationshipTitlesForDetail(currentDetail, "tag"));
  const looseTags = $derived(tags.filter((tag) => !tagRelationshipForTitle(tag)));
  const imageGroups = $derived(groupReviewImages(proposal));
  const artworkCandidateCount = $derived(imageGroups.reduce((count, group) => count + group.images.length, 0));
  const selectedTagCount = $derived(Object.values(selectedTags).filter(Boolean).length);
  const selectedChildCount = $derived(
    children.filter((child) => store.isReviewProposalSelected(child.proposalId)).length,
  );
  const contextPosterUrl = $derived(proposalImageUrl(["poster", "thumbnail", "cover", "logo"]));

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
      defaultTagSelection();
    store.setReviewFieldSelections(proposal.proposalId, selectedFields);
    store.setReviewImageSelections(proposal.proposalId, selectedImages);
    store.setReviewTagSelections(proposal.proposalId, selectedTags);
  });

  function hasField(field: string): boolean {
    return proposalHasField(proposal, field);
  }

  function fieldValue(field: string): string {
    return proposalFieldValue(proposal, field);
  }

  function defaultTagSelection(): Record<string, boolean> {
    return Object.fromEntries((proposal.patch?.tags ?? []).map((tag) => [tag, true]));
  }

  function currentEntityFallback(): EntityCard {
    return {
      id: currentDetailEntityId ?? proposal.targetEntityId ?? proposal.proposalId,
      kind: proposal.targetKind,
      title: currentDetail?.title ?? "",
      parentEntityId: null,
      sortOrder: null,
      coverUrl: null,
      hoverKind: "none",
      hoverUrl: null,
      hoverImages: [],
      meta: [],
      rating: null,
      isFavorite: false,
      isNsfw: false,
      isOrganized: false,
    };
  }

  function currentFieldValue(field: string): string {
    return currentFieldValueForReview(currentEntityFallback(), currentDetail, field);
  }

  function proposalImageUrl(kinds: string[]): string | null {
    const images = reviewableImages(proposal.images ?? [], proposal.targetKind);
    for (const kind of kinds) {
      const image = images.find((candidate) => candidate.kind === kind);
      if (image) return reviewImagePreviewUrl(image, proposal.targetKind);
    }
    return images[0] ? reviewImagePreviewUrl(images[0], proposal.targetKind) : null;
  }

  function preferredProposalImage(result: EntityMetadataProposal): ImageCandidate | null {
    const images = reviewableImages(result.images ?? [], result.targetKind);
    return images.find((image) => image.kind === "poster") ??
      images.find((image) => image.kind === "thumbnail") ??
      images[0] ??
      null;
  }

  function preferredRelationshipImage(result: EntityMetadataProposal): ImageCandidate | null {
    return result.images.find((image) => image.kind === "poster") ??
      result.images.find((image) => image.kind === "thumbnail") ??
      result.images.find((image) => image.kind === "logo") ??
      result.images[0] ??
      null;
  }

  function roleLabel(credit: CreditPatch | null | undefined): string {
    const role = credit?.role?.trim();
    if (!role) return "Cast";
    return role.replaceAll("-", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
  }

  function proposalTitle(result: EntityMetadataProposal): string {
    return result.patch?.title?.trim() || result.targetKind;
  }

  function relationshipKindLabel(kind: string): string {
    return kind.replaceAll("-", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
  }

  function relationshipIcon(kind: string): EntityThumbnailMetaIcon {
    if (kind === "studio") return "studio";
    if (kind === "tag") return "tag";
    if (kind === "person") return "person";
    return "collection";
  }

  function relationshipStatusLabel(result: EntityMetadataProposal): string {
    if (result.targetKind === "tag") {
      return isNewRelationshipTitle(proposalTitle(result), existingTagTitles) ? "New" : "Existing";
    }

    return relationshipKindLabel(result.targetKind);
  }

  function relationshipCard(result: EntityMetadataProposal): EntityThumbnailCard {
    const image = preferredRelationshipImage(result);
    const title = proposalTitle(result);
    return {
      entity: { id: result.proposalId, kind: result.targetKind, title, parentEntityId: null, sortOrder: null, capabilities: [], childrenByKind: [], relationships: [] },
      aspectRatio: result.targetKind === "studio" ? "wide" : result.targetKind === "person" ? { width: 4, height: 5 } : "square",
      cover: image ? { src: reviewImagePreviewUrl(image, result.targetKind), alt: title } : null,
      hover: { kind: "none" },
      subtitle: relationshipKindLabel(result.targetKind),
      meta: [{ icon: relationshipIcon(result.targetKind), label: relationshipStatusLabel(result) }],
    };
  }

  function tagRelationshipForTitle(tag: string): EntityMetadataProposal | null {
    return relationships.find((relationship) =>
      relationship.targetKind === "tag" &&
      proposalTitle(relationship).localeCompare(tag, undefined, { sensitivity: "accent" }) === 0,
    ) ?? null;
  }

  function setRelationshipSelected(result: EntityMetadataProposal, selected: boolean) {
    store.setReviewProposalSelected(result.proposalId, selected);
    if (result.targetKind === "tag") {
      setTagSelected(proposalTitle(result), selected);
    }
  }

  function setTagSelected(tag: string, selected: boolean) {
    selectedTags = {
      ...selectedTags,
      [tag]: selected,
    };
    store.setReviewTagSelected(proposal.proposalId, tag, selected);
    const relationship = tagRelationshipForTitle(tag);
    if (relationship) {
      store.setReviewProposalSelected(relationship.proposalId, selected);
    }
  }

  function setFieldSelected(field: string, selected: boolean) {
    selectedFields = {
      ...selectedFields,
      [field]: selected,
    };
    store.setReviewFieldSelected(proposal.proposalId, field, selected);
  }

  function setAllFields(selected: boolean) {
    selectedFields = {
      ...selectedFields,
      ...Object.fromEntries(DIFF_FIELD_KEYS.map((k) => [k, selected ? hasField(k) : false])),
    };
    store.setReviewFieldSelections(proposal.proposalId, selectedFields);
  }

  function setImageSelected(kind: string, url: string | null) {
    selectedImages = {
      ...selectedImages,
      [kind]: url,
    };
    store.setReviewImageSelected(proposal.proposalId, kind, url);
  }

  function setChildSelected(child: EntityMetadataProposal, selected: boolean) {
    store.setReviewProposalSelected(child.proposalId, selected);
  }

  function goBackToParent() {
    if (ancestors.length <= 1) {
      store.navigateTo({ kind: "review-parent", entity, proposal: parentProposal });
      return;
    }

    const nextAncestors = ancestors.slice(0, -1);
    const grandParent = nextAncestors[nextAncestors.length - 1];
    store.navigateTo({
      kind: "review-child",
      entity,
      proposal: parentProposal,
      parentProposal: grandParent,
      ancestors: nextAncestors,
    });
  }

  function goToSibling(sibling: EntityMetadataProposal) {
    store.navigateTo({
      kind: "review-child",
      entity,
      proposal: sibling,
      parentProposal,
      ancestors,
    });
  }

  function goToChild(child: EntityMetadataProposal) {
    store.navigateTo({
      kind: "review-child",
      entity,
      proposal: child,
      parentProposal: proposal,
      ancestors: [...ancestors, proposal],
    });
  }
</script>

<div class="flex flex-col gap-4">
  <!-- Nav -->
  <div class="flex items-center gap-3">
    <button
      type="button"
      class="inline-flex h-8 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary"
      onclick={goBackToParent}
    >
      <ChevronLeft class="h-3.5 w-3.5" />
      Back to {parentProposal.patch?.title ?? entity.title}
    </button>
    <div class="flex-1"></div>
    <!-- Sibling nav -->
    {#if parentChildren.length > 1}
      <div class="flex items-center gap-1.5">
        <button
          type="button"
          class="inline-flex h-7 w-7 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30"
          disabled={!prevChild}
          onclick={() => prevChild && goToSibling(prevChild)}
        >
          <ChevronLeft class="h-3.5 w-3.5" />
        </button>
        <span class="font-mono text-[0.72rem] text-text-muted">
          {currentIndex + 1} of {parentChildren.length}
        </span>
        <button
          type="button"
          class="inline-flex h-7 w-7 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30"
          disabled={!nextChild}
          onclick={() => nextChild && goToSibling(nextChild)}
        >
          <ChevronRight class="h-3.5 w-3.5" />
        </button>
      </div>
    {/if}
  </div>

  <!-- Context bar -->
  <div class="grid grid-cols-[auto_1fr_auto_auto] items-center gap-4 rounded-sm border border-border-subtle bg-surface-1 p-3.5 shadow-well">
    {#if contextPosterUrl}
      <img src={contextPosterUrl} alt="" class="h-16 w-11 rounded-xs object-cover" decoding="async" />
    {:else}
      <div class="grid h-16 w-11 place-items-center rounded-xs bg-surface-3">
        <Layers class="h-5 w-5 text-text-disabled" />
      </div>
    {/if}
    <div class="min-w-0">
      <div class="flex items-baseline gap-2">
        <h2 class="truncate">{proposal.patch?.title ?? `Child ${currentIndex + 1}`}</h2>
        <span class="rounded-xs border border-phosphor-600/20 bg-surface-3 px-1.5 py-0.5 font-mono text-[0.6rem] text-phosphor-600">
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
      <div class="flex items-center gap-1.5">
        <StatusLed status="accent" pulse />
        <span class="text-[0.82rem] text-text-primary">{proposal.provider}</span>
      </div>
    </div>
  </div>

  <!-- Field diff -->
  <section class="surface-panel overflow-hidden">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <Info class="h-3.5 w-3.5 text-text-accent" />
      <span class="text-kicker text-text-accent">Field diff · {proposal.patch?.title ?? ""}</span>
      <span class="font-mono text-[0.7rem] text-text-muted">
        {DIFF_FIELD_KEYS.filter((k) => selectedFields[k]).length}/{DIFF_FIELD_KEYS.filter((k) => hasField(k)).length} accepted
      </span>
      <div class="flex-1"></div>
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
    </header>

    <div class="hidden grid-cols-[auto_110px_1fr_1fr] items-center gap-3 border-b border-border-default bg-surface-2 px-3.5 py-1.5 md:grid">
      <span class="w-5"></span>
      <span class="text-kicker">Field</span>
      <span class="text-kicker">Current</span>
      <span class="text-kicker text-text-accent">Proposed</span>
    </div>

    {#each DIFF_FIELD_KEYS as field (field)}
      {#if hasField(field)}
        {@const current = currentFieldValue(field)}
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
            <div class="mt-1 text-[0.82rem] leading-snug text-text-primary md:mt-0">{fieldValue(field)}</div>
          </div>
        </div>
      {/if}
    {/each}
  </section>

  <!-- Credits (inherited) -->
  {#if credits.length > 0}
    <section class="surface-panel identify-lazy-section overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Users class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Credits</span>
        <span class="font-mono text-[0.7rem] text-text-muted">
          inherited · {credits.filter((credit) => store.isReviewProposalSelected(credit.proposalId)).length} of {credits.length} selected
        </span>
      </header>
      <div class="identify-thumbnail-grid grid grid-cols-2 gap-2 p-3.5 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6">
        {#each credits as credit (credit.proposalId)}
          {@const scopedCredit = scopedCreditForProposal(proposal, credit)}
          {@const image = preferredProposalImage(credit)}
          {@const card = {
            entity: { id: credit.proposalId, kind: "person", title: credit.patch?.title ?? "", parentEntityId: null, sortOrder: null, capabilities: [], childrenByKind: [], relationships: [] },
            aspectRatio: { width: 4, height: 5 },
            cover: image ? { src: reviewImagePreviewUrl(image, credit.targetKind), alt: credit.patch?.title ?? "" } : null,
            hover: { kind: "none" } as const,
            subtitle: scopedCredit?.character ? `as ${scopedCredit.character}` : roleLabel(scopedCredit),
            meta: [{ icon: "person" as const, label: roleLabel(scopedCredit) }],
          }}
          <EntityThumbnail
            {card}
            linkable={false}
            onActivate={() => goToChild(credit)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(credit.proposalId)}
            onSelectedChange={(selected) => setRelationshipSelected(credit, selected)}
          />
        {/each}
      </div>
    </section>
  {/if}

  <!-- Relationships -->
  {#if nonCreditRelationships.length > 0}
    <section class="surface-panel identify-lazy-section overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Layers class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Relationships</span>
        <span class="font-mono text-[0.7rem] text-text-muted">
          {nonCreditRelationships.filter((relationship) => store.isReviewProposalSelected(relationship.proposalId)).length} of {nonCreditRelationships.length} selected
        </span>
      </header>
      <div class="identify-thumbnail-grid grid grid-cols-2 gap-2 p-3.5 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
        {#each nonCreditRelationships as relationship (relationship.proposalId)}
          <EntityThumbnail
            card={relationshipCard(relationship)}
            linkable={false}
            onActivate={() => goToChild(relationship)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(relationship.proposalId)}
            onSelectedChange={(selected) => setRelationshipSelected(relationship, selected)}
          />
        {/each}
      </div>
    </section>
  {/if}

  <!-- Tags -->
  {#if looseTags.length > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Tag class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Tags</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{selectedTagCount} of {tags.length} selected</span>
      </header>
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
              {isExisting ? "Existing" : "New"}
            </span>
          </button>
        {/each}
      </div>
    </section>
  {/if}

  <!-- Artwork -->
  {#if artworkCandidateCount > 0}
    <section class="surface-panel identify-lazy-section overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Images class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Artwork</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{artworkCandidateCount} candidates</span>
      </header>
      <div class="p-3.5">
        {#each imageGroups as group (group.kind)}
          <div class="mb-3 last:mb-0">
            <div class="mb-2 flex items-center gap-2">
              <span class="text-kicker">{group.kind}</span>
            </div>
            <div class="grid gap-1.5" class:grid-cols-2={group.kind === "backdrop"} class:grid-cols-3={group.kind !== "backdrop"}>
              {#each group.images as image (image.url)}
                <button
                  type="button"
                  class={cn(
                    "identify-artwork-tile relative overflow-hidden rounded-xs border bg-surface-3 transition-all",
                    selectedImages[group.kind] === image.url
                      ? "border-border-accent-strong shadow-[0_0_16px_rgba(242,194,106,0.2)]"
                      : "border-border-default hover:border-border-accent",
                  )}
                  style="aspect-ratio: {group.kind === 'poster' ? '2/3' : group.kind === 'backdrop' ? '16/9' : '2/1'};"
                  onclick={() => setImageSelected(group.kind, selectedImages[group.kind] === image.url ? null : image.url)}
                >
                  <img
                    src={reviewImagePreviewUrl(image, proposal.targetKind)}
                    alt=""
                    class="h-full w-full object-cover"
                    loading="lazy"
                    decoding="async"
                    fetchpriority="low"
                  />
                  {#if selectedImages[group.kind] === image.url}
                    <div class="absolute right-1 top-1">
                      <span class="grid h-4 w-4 place-items-center rounded-xs bg-accent-500 text-[#0b0b0c]">
                        <Check class="h-2.5 w-2.5" />
                      </span>
                    </div>
                  {/if}
                </button>
              {/each}
            </div>
          </div>
        {/each}
      </div>
    </section>
  {/if}

  <!-- Children (episodes) -->
  {#if children.length > 0}
    <section class="surface-panel identify-lazy-section overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Layers class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Children</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{selectedChildCount} of {children.length} selected</span>
      </header>
      <div class="identify-thumbnail-grid grid grid-cols-2 gap-2 p-3.5 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
        {#each children as child, i (child.proposalId)}
          {@const childImage = preferredProposalImage(child)}
          {@const childCard = {
            entity: { id: child.proposalId, kind: child.targetKind, title: child.patch?.title ?? `Episode ${i + 1}`, parentEntityId: null, sortOrder: i, capabilities: [], childrenByKind: [], relationships: [] },
            aspectRatio: "video" as const,
            cover: childImage ? { src: reviewImagePreviewUrl(childImage, child.targetKind), alt: child.patch?.title ?? "" } : null,
            hover: { kind: "none" } as const,
            subtitle: child.targetKind,
            custom: child.patch?.positions?.episode ? { bottomLeft: { label: `E${String(child.patch?.positions.episode).padStart(2, "0")}` } } : undefined,
            meta: child.confidence ? [{ icon: "count" as const, label: `${Math.round(child.confidence * 100)}%` }] : [],
          }}
          <EntityThumbnail
            card={childCard}
            linkable={false}
            onActivate={() => goToChild(child)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(child.proposalId)}
            onSelectedChange={(selected) => setChildSelected(child, selected)}
          />
        {/each}
      </div>
    </section>
  {/if}

  <!-- Action footer -->
  <div class="flex items-center gap-3 py-2">
    <button
      type="button"
      class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-3 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary"
      onclick={goBackToParent}
    >
      <ChevronLeft class="h-3.5 w-3.5" />
      Back to {parentProposal.patch?.title ?? entity.title}
    </button>
    {#if parentChildren.length > 1}
      <div class="flex items-center gap-1">
        {#if prevChild}
          <button
            type="button"
            class="inline-flex h-8 items-center gap-1 rounded-xs border border-border-default bg-surface-2 px-2 text-[0.72rem] text-text-muted transition-colors hover:bg-surface-3"
            onclick={() => prevChild && goToSibling(prevChild)}
          >
            <ChevronLeft class="h-3 w-3" />
          </button>
        {/if}
        <span class="px-1 font-mono text-[0.7rem] text-text-muted">
          {currentIndex + 1} of {parentChildren.length}
        </span>
        {#if nextChild}
          <button
            type="button"
            class="inline-flex h-8 items-center gap-1 rounded-xs border border-border-default bg-surface-2 px-2 text-[0.72rem] text-text-muted transition-colors hover:bg-surface-3"
            onclick={() => nextChild && goToSibling(nextChild)}
          >
            Next
            <ChevronRight class="h-3 w-3" />
          </button>
        {/if}
      </div>
    {/if}
  </div>
</div>

<style>
  .identify-lazy-section {
    content-visibility: auto;
    contain-intrinsic-size: auto 36rem;
  }

  .identify-thumbnail-grid {
    content-visibility: auto;
    contain-intrinsic-size: auto 28rem;
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
