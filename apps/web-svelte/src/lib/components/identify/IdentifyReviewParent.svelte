<script lang="ts">
  import {
    Check,
    ChevronDown,
    ChevronRight,
    ChevronUp,
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
  import IdentifyReviewSection from "./IdentifyReviewSection.svelte";
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
    structuralChildProposals,
    relationshipProposals,
    relationshipTitlesForDetail,
    reviewDiffFieldKeys,
    reviewableImages,
    reviewFieldLabels,
    scopedCreditForProposal,
  } from "$lib/components/identify-review";
  import type {
    CreditPatch,
    EntityMetadataProposal,
    ImageCandidate,
  } from "$lib/api/identify";
  import type { EntityCard, EntityDetailCard } from "$lib/api/prismedia";
  import type { EntityThumbnailCard, EntityThumbnailMetaIcon } from "$lib/entities/entity-thumbnail";
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
  const credits = $derived(
    relationships.filter((r) => r.targetKind === "person"),
  );
  const nonCreditRelationships = $derived(
    relationships.filter((r) => r.targetKind !== "person"),
  );
  const tags = $derived(proposal.patch?.tags ?? []);
  const existingTagTitles = $derived(relationshipTitles("tag"));
  const looseTags = $derived(tags.filter((tag) => !tagRelationshipForTitle(tag)));
  const imageGroups = $derived(groupReviewImages(proposal));
  const selectedTagCount = $derived(Object.values(selectedTags).filter(Boolean).length);
  const selectedRelationshipCount = $derived(
    relationships.filter((relationship) => store.isReviewProposalSelected(relationship.proposalId)).length,
  );
  const selectedChildCount = $derived(
    children.filter((child) => store.isReviewProposalSelected(child.proposalId)).length,
  );
  const nextQueueItem = $derived(store.nextQueueItem(entity.id));
  const queueIndex = $derived(store.queue.findIndex((item) => item.entityId === entity.id));
  const prevQueueNavItem = $derived(queueIndex > 0 ? store.queue[queueIndex - 1] : null);
  const nextQueueNavItem = $derived(queueIndex >= 0 && queueIndex < store.queue.length - 1 ? store.queue[queueIndex + 1] : null);
  const contextPosterUrl = $derived(selectedProposalImageUrl(proposal, ["poster", "thumbnail", "cover"]) ?? entity.coverUrl ?? proposalImageUrl(["poster", "thumbnail", "cover"]));

  $effect(() => {
    if (reviewStateProposalId === proposal.proposalId) return;
    reviewStateProposalId = proposal.proposalId;
    store.beginProposalReview(proposal);
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

  function currentFieldValue(field: string): string {
    return currentFieldValueForReview(entity, detail, field);
  }

  function relationshipTitles(kind: string): string[] {
    return relationshipTitlesForDetail(detail, kind);
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
    const selected = selectedProposalImage(result, ["poster", "thumbnail", "cover", "logo"]);
    if (selected) return selected;
    const images = reviewableImages(result.images ?? [], result.targetKind);
    return images.find((image) => image.kind === "poster") ??
      images.find((image) => image.kind === "thumbnail") ??
      images[0] ??
      null;
  }

  function preferredRelationshipImage(result: EntityMetadataProposal): ImageCandidate | null {
    const selected = selectedProposalImage(result, ["poster", "thumbnail", "logo", "cover"]);
    if (selected) return selected;
    return result.images.find((image) => image.kind === "poster") ??
      result.images.find((image) => image.kind === "thumbnail") ??
      result.images.find((image) => image.kind === "logo") ??
      result.images[0] ??
      null;
  }

  function selectedProposalImage(result: EntityMetadataProposal, kinds: string[]): ImageCandidate | null {
    const images = reviewableImages(result.images ?? [], result.targetKind);
    const selections = result.proposalId === proposal.proposalId
      ? selectedImages
      : store.getReviewImageSelections(result.proposalId);
    if (!selections) return null;

    for (const kind of kinds) {
      const url = selections[kind];
      if (!url) continue;
      const image = images.find((candidate) => candidate.kind === kind && candidate.url === url);
      if (image) return image;
    }

    return null;
  }

  function selectedProposalImageUrl(result: EntityMetadataProposal, kinds: string[]): string | null {
    const selected = selectedProposalImage(result, kinds);
    return selected ? reviewImagePreviewUrl(selected, result.targetKind) : null;
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
    if (result.targetEntityId) return "Merge";
    return isNewRelationshipTitle(proposalTitle(result), relationshipTitles(result.targetKind)) ? "New" : "Merge";
  }

  function proposalStatusCustom(result: EntityMetadataProposal): EntityThumbnailCard["custom"] {
    const label = relationshipStatusLabel(result);
    return { bottomLeft: { label, title: `${label} ${relationshipKindLabel(result.targetKind)}` } };
  }

  function childStatusCustom(child: EntityMetadataProposal): EntityThumbnailCard["custom"] {
    const label = "Matched";
    return { bottomLeft: { label, title: `${label} ${relationshipKindLabel(child.targetKind)}` } };
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
      custom: proposalStatusCustom(result),
      meta: [{ icon: relationshipIcon(result.targetKind), label: relationshipKindLabel(result.targetKind) }],
    };
  }

  function childMeta(child: EntityMetadataProposal): EntityThumbnailCard["meta"] {
    const meta: EntityThumbnailCard["meta"] = [];
    const episode = child.patch?.positions?.episode;
    if (episode) meta.push({ icon: "count", label: `E${String(episode).padStart(2, "0")}` });
    if (child.confidence) meta.push({ icon: "count", label: `${Math.round(child.confidence * 100)}%` });
    return meta;
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
  <!-- Context bar -->
  <div class="grid grid-cols-[auto_1fr_auto_auto_auto] items-center gap-4 rounded-sm border border-border-subtle bg-surface-1 p-3.5 shadow-well">
    {#if contextPosterUrl}
      <img src={contextPosterUrl} alt="" class="h-16 w-11 rounded-xs object-cover" decoding="async" />
    {:else}
      <div class="grid h-16 w-11 place-items-center rounded-xs bg-surface-3">
        <Layers class="h-5 w-5 text-text-disabled" />
      </div>
    {/if}
    <div class="min-w-0">
      <div class="flex items-baseline gap-2">
        <h2 class="truncate">{proposal.patch?.title ?? entity.title}</h2>
        <span class="rounded-xs border border-phosphor-600/20 bg-surface-3 px-1.5 py-0.5 font-mono text-[0.6rem] text-phosphor-600">
          {entity.kind}
        </span>
      </div>
      <div class="mt-0.5 truncate font-mono text-[0.7rem] text-text-muted">{entity.title}</div>
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
  <IdentifyReviewSection
    panelId={`base-fields-${proposal.proposalId}`}
    title="Base fields"
    meta={`${DIFF_FIELD_KEYS.filter((k) => selectedFields[k]).length} of ${DIFF_FIELD_KEYS.filter((k) => hasField(k)).length} accepted`}
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
            <div class="mt-1 text-[0.82rem] leading-snug text-text-primary md:mt-0">
              {fieldValue(field)}
            </div>
          </div>
        </div>
      {/if}
    {/each}
  </IdentifyReviewSection>

  <!-- Credits -->
  {#if credits.length > 0}
    <IdentifyReviewSection
      panelId={`credits-${proposal.proposalId}`}
      title="Credits"
      meta={`${credits.filter((credit) => store.isReviewProposalSelected(credit.proposalId)).length} of ${credits.length} selected`}
      lazy
    >
      {#snippet icon()}
        <Users class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
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
            custom: proposalStatusCustom(credit),
            meta: [{ icon: "person" as const, label: roleLabel(scopedCredit) }],
          }}
          <EntityThumbnail
            {card}
            linkable={false}
            onActivate={() => walkChild(credit)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(credit.proposalId)}
            onSelectedChange={(selected) => setRelationshipSelected(credit, selected)}
          />
        {/each}
      </div>
    </IdentifyReviewSection>
  {/if}

  <!-- Relationships -->
  {#if nonCreditRelationships.length > 0}
    <IdentifyReviewSection
      panelId={`relationships-${proposal.proposalId}`}
      title="Relationships"
      meta={`${nonCreditRelationships.filter((relationship) => store.isReviewProposalSelected(relationship.proposalId)).length} of ${nonCreditRelationships.length} selected`}
      lazy
    >
      {#snippet icon()}
        <Layers class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
      <div class="identify-thumbnail-grid grid grid-cols-2 gap-2 p-3.5 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
        {#each nonCreditRelationships as relationship (relationship.proposalId)}
          <EntityThumbnail
            card={relationshipCard(relationship)}
            linkable={false}
            onActivate={() => walkChild(relationship)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(relationship.proposalId)}
            onSelectedChange={(selected) => setRelationshipSelected(relationship, selected)}
          />
        {/each}
      </div>
    </IdentifyReviewSection>
  {/if}

  <!-- Artwork — one card per kind -->
  {#each imageGroups as group (group.kind)}
    <IdentifyReviewSection
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
    </IdentifyReviewSection>
  {/each}

  <!-- Tags -->
  {#if looseTags.length > 0}
    <IdentifyReviewSection
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
    </IdentifyReviewSection>
  {/if}

  <!-- Children -->
  {#if children.length > 0}
    <IdentifyReviewSection
      panelId={`children-${proposal.proposalId}`}
      title="Children"
      meta={`${selectedChildCount} of ${children.length} selected`}
      lazy
    >
      {#snippet icon()}
        <Layers class="h-3.5 w-3.5 text-text-accent" />
      {/snippet}
      <div class="identify-thumbnail-grid grid grid-cols-2 gap-2 p-3.5 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
        {#each children as child, i (child.proposalId)}
          {@const childImage = preferredProposalImage(child)}
          {@const childCard = {
            entity: { id: child.proposalId, kind: child.targetKind, title: child.patch?.title ?? `Child ${i + 1}`, parentEntityId: null, sortOrder: i, capabilities: [], childrenByKind: [], relationships: [] },
            aspectRatio: "poster" as const,
            cover: childImage ? { src: reviewImagePreviewUrl(childImage, child.targetKind), alt: child.patch?.title ?? "" } : null,
            hover: { kind: "none" } as const,
            subtitle: child.targetKind,
            custom: childStatusCustom(child),
            meta: childMeta(child),
          }}
          <EntityThumbnail
            card={childCard}
            linkable={false}
            onActivate={() => walkChild(child)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(child.proposalId)}
            onSelectedChange={(selected) => setChildSelected(child, selected)}
          />
        {/each}
      </div>
    </IdentifyReviewSection>
  {/if}

  <!-- Action footer -->
  <div class="flex flex-col gap-2 py-2 md:flex-row md:items-center md:gap-3">
    <!-- Top row on mobile: cancel + summary -->
    <div class="flex items-center gap-3">
      <button
        type="button"
        class="inline-flex h-10 flex-1 items-center justify-center gap-1.5 rounded-xs border border-border-default bg-transparent text-[0.78rem] text-text-muted transition-colors hover:border-error/50 hover:text-error-text md:h-9 md:flex-none md:px-3"
        onclick={() => void store.deleteQueueItem(entity.id)}
      >
        <X class="h-3.5 w-3.5" />
        Cancel
      </button>
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
    </div>

    <span class="hidden font-mono text-[0.7rem] text-text-muted md:inline">
      {Object.values(selectedFields).filter(Boolean).length} fields
      · {Object.values(selectedImages).filter(Boolean).length} imgs
      · {selectedRelationshipCount} rels
      · {selectedTagCount} tags
      · {selectedChildCount} children
    </span>
    <div class="hidden flex-1 md:block"></div>

    <!-- Accept buttons: full-width stacked on mobile -->
    <div class="flex flex-col gap-2 md:flex-row md:gap-3">
      <button
        type="button"
        class="inline-flex h-10 items-center justify-center gap-1.5 rounded-xs border border-border-accent-strong px-3 text-[0.78rem] text-text-primary transition-all disabled:cursor-not-allowed disabled:opacity-40 md:h-9"
        style="background: linear-gradient(135deg, rgba(242,194,106,0.24), rgba(242,194,106,0.1)); box-shadow: 0 0 18px rgba(242,194,106,0.16);"
        disabled={store.applying}
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
          disabled={store.applying}
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

<style>
  .identify-thumbnail-grid {
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
