<script lang="ts">
  import {
    Check,
    ChevronLeft,
    ChevronRight,
    Images,
    Info,
    Layers,
    Loader2,
    SkipForward,
    Tag,
    Users,
    X,
  } from "@lucide/svelte";
  import { cn, StatusLed } from "@prismedia/ui-svelte";
  import { getCapability, getDescription, getImagesCapability } from "$lib/api/capabilities";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { CAPABILITY_KIND } from "$lib/entities/entity-codes";
  import {
    buildRootReviewApplyPayload,
    defaultImageSelectionForReview,
    isNewRelationshipTitle,
    structuralChildProposals,
    relationshipProposals,
    reviewableImages,
    reviewFieldKeys,
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

  const FIELD_KEYS = reviewFieldKeys;

  const FIELD_LABELS: Record<string, string> = {
    title: "Title",
    description: "Description",
    externalIds: "Provider IDs",
    urls: "Links",
    tags: "Tags",
    studio: "Studio",
    credits: "Credits",
    dates: "Dates",
    stats: "Stats",
    positions: "Positions",
    classification: "Classification",
    images: "Artwork",
  };

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
  const imageGroups = $derived(groupImages(reviewableImages(proposal.images ?? [])));
  const artworkCandidateCount = $derived(imageGroups.reduce((count, group) => count + group.images.length, 0));
  const selectedTagCount = $derived(Object.values(selectedTags).filter(Boolean).length);
  const selectedRelationshipCount = $derived(
    relationships.filter((relationship) => store.isReviewProposalSelected(relationship.proposalId)).length,
  );
  const selectedChildCount = $derived(
    children.filter((child) => store.isReviewProposalSelected(child.proposalId)).length,
  );
  const nextQueueItem = $derived(store.nextQueueItem(entity.id));
  const contextPosterUrl = $derived(entity.coverUrl ?? proposalImageUrl(["poster", "thumbnail", "cover"]));

  $effect(() => {
    if (reviewStateProposalId === proposal.proposalId) return;
    reviewStateProposalId = proposal.proposalId;
    store.beginProposalReview(proposal);
    selectedFields = store.getReviewFieldSelections(proposal.proposalId) ??
      Object.fromEntries(FIELD_KEYS.map((k) => [k, hasField(k)]));
    selectedImages = store.getReviewImageSelections(proposal.proposalId) ??
      defaultImageSelectionForReview(proposal);
    selectedTags = store.getReviewTagSelections(proposal.proposalId) ??
      defaultTagSelection();
    store.setReviewFieldSelections(proposal.proposalId, selectedFields);
    store.setReviewImageSelections(proposal.proposalId, selectedImages);
    store.setReviewTagSelections(proposal.proposalId, selectedTags);
  });

  function hasField(field: string): boolean {
    return fieldValue(field).trim().length > 0;
  }

  function fieldValue(field: string): string {
    const patch = proposal.patch;
    if (!patch) return "";
    if (field === "title") return patch.title ?? "";
    if (field === "description") return patch.description ?? "";
    if (field === "externalIds") return Object.entries(patch.externalIds ?? {}).map(([k, v]) => `${k}: ${v}`).join(", ");
    if (field === "urls") return (patch.urls ?? []).join(", ");
    if (field === "tags") return (patch.tags ?? []).join(", ");
    if (field === "studio") return patch.studio ?? "";
    if (field === "credits") return (patch.credits ?? []).map((c) => c.character ? `${c.name} as ${c.character}` : c.name).join(", ");
    if (field === "dates") return Object.entries(patch.dates ?? {}).map(([k, v]) => `${k}: ${v}`).join(", ");
    if (field === "stats") return Object.entries(patch.stats ?? {}).map(([k, v]) => `${k}: ${v}`).join(", ");
    if (field === "positions") return Object.entries(patch.positions ?? {}).map(([k, v]) => `${k}: ${v}`).join(", ");
    if (field === "classification") return patch.classification ?? "";
    if (field === "images") return imageGroups.map((group) => `${group.kind} (${group.images.length})`).join(", ");
    return "";
  }

  function groupImages(images: ImageCandidate[]): Array<{ kind: string; images: ImageCandidate[] }> {
    const groups: Record<string, ImageCandidate[]> = {};
    for (const image of images) {
      groups[image.kind] = [...(groups[image.kind] ?? []), image];
    }
    return Object.entries(groups).map(([kind, imgs]) => ({ kind, images: imgs }));
  }

  function defaultTagSelection(): Record<string, boolean> {
    return Object.fromEntries((proposal.patch?.tags ?? []).map((tag) => [tag, true]));
  }

  function currentFieldValue(field: string): string {
    if (field === "title") return detail?.title ?? entity.title ?? "";
    if (!detail) return "";

    const capabilities = detail.capabilities ?? [];
    if (field === "description") return getDescription(capabilities) ?? "";
    if (field === "externalIds") {
      const links = getCapability(capabilities, CAPABILITY_KIND.links);
      return (links?.externalIds ?? []).map((externalId) => `${externalId.provider}: ${externalId.value}`).join(", ");
    }
    if (field === "urls") {
      const links = getCapability(capabilities, CAPABILITY_KIND.links);
      return (links?.urls ?? []).map((url) => url.value).join(", ");
    }
    if (field === "tags") return relationshipTitles("tag").join(", ");
    if (field === "studio") return relationshipTitles("studio")[0] ?? "";
    if (field === "credits") return relationshipTitles("person").join(", ");
    if (field === "dates") {
      const dates = getCapability(capabilities, CAPABILITY_KIND.dates);
      return (dates?.items ?? []).map((item) => `${item.code}: ${item.value}`).join(", ");
    }
    if (field === "stats") {
      const stats = getCapability(capabilities, CAPABILITY_KIND.stats);
      return (stats?.items ?? []).map((item) => `${item.code}: ${item.value}`).join(", ");
    }
    if (field === "positions") {
      const positions = getCapability(capabilities, CAPABILITY_KIND.position);
      return (positions?.items ?? []).map((item) => `${item.code}: ${item.value}`).join(", ");
    }
    if (field === "classification") {
      const classification = getCapability(capabilities, CAPABILITY_KIND.classification);
      return classification?.value ?? "";
    }
    if (field === "images") {
      const images = getImagesCapability(capabilities);
      return (images?.items ?? [])
        .filter((image) => image.kind !== "source")
        .map((image) => String(image.kind))
        .join(", ");
    }
    return "";
  }

  function relationshipTitles(kind: string): string[] {
    return (detail?.relationships ?? [])
      .filter((group) => group.kind === kind)
      .flatMap((group) => group.entities)
      .map((item) => item.title)
      .filter((title): title is string => Boolean(title));
  }

  function proposalImageUrl(kinds: string[]): string | null {
    const images = reviewableImages(proposal.images ?? []);
    for (const kind of kinds) {
      const image = images.find((candidate) => candidate.kind === kind);
      if (image) return image.url;
    }
    return images[0]?.url ?? null;
  }

  function preferredProposalImage(result: EntityMetadataProposal): ImageCandidate | null {
    const images = reviewableImages(result.images ?? []);
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
      cover: image ? { src: image.url, alt: title } : null,
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
    selectedFields = Object.fromEntries(FIELD_KEYS.map((k) => [k, selected ? hasField(k) : false]));
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
  <!-- Breadcrumb nav -->
  <div class="flex items-center gap-3">
    <button
      type="button"
      class="inline-flex h-8 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary"
      onclick={() => store.navigateToDashboard()}
    >
      <ChevronLeft class="h-3.5 w-3.5" />
      Back
    </button>
    <div class="flex items-center gap-1.5 text-[0.78rem]">
      <span class="text-text-muted">Identify</span>
      <ChevronRight class="h-3 w-3 text-text-disabled" />
      <span class="font-heading font-semibold text-text-primary">{proposal.patch?.title ?? entity.title}</span>
      <span class="rounded-xs border border-border-accent bg-accent-950/30 px-1.5 py-0.5 font-mono text-[0.6rem] text-text-accent">
        {entity.kind}
      </span>
    </div>
    <div class="flex-1"></div>
    <button
      type="button"
      class="inline-flex h-8 items-center gap-1 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.72rem] text-text-muted transition-colors hover:bg-surface-3"
      onclick={() => store.navigateToDashboard()}
    >
      <SkipForward class="h-3.5 w-3.5" />
      Skip
    </button>
  </div>

  <!-- Context bar -->
  <div class="grid grid-cols-[auto_1fr_auto_auto_auto] items-center gap-4 rounded-sm border border-border-subtle bg-surface-1 p-3.5 shadow-well">
    {#if contextPosterUrl}
      <img src={contextPosterUrl} alt="" class="h-16 w-11 rounded-xs object-cover" />
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
      <div class="flex items-center gap-1.5">
        <StatusLed status="accent" pulse />
        <span class="text-[0.82rem] text-text-primary">{proposal.provider}</span>
      </div>
    </div>
    <div class="hidden flex-col items-end gap-0.5 md:flex">
      <span class="text-kicker">Reason</span>
      <span class="text-[0.74rem] text-text-secondary">{proposal.matchReason ?? "—"}</span>
    </div>
  </div>

  <!-- Field diff -->
  <section class="surface-panel overflow-hidden">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <Info class="h-3.5 w-3.5 text-text-accent" />
      <span class="text-kicker text-text-accent">Field diff</span>
      <span class="font-mono text-[0.7rem] text-text-muted">
        {Object.values(selectedFields).filter(Boolean).length} of {FIELD_KEYS.filter((k) => hasField(k)).length} accepted
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

    <!-- Diff header -->
    <div class="hidden grid-cols-[auto_110px_1fr_1fr] items-center gap-3 border-b border-border-default bg-surface-2 px-3.5 py-1.5 md:grid">
      <span class="w-5"></span>
      <span class="text-kicker">Field</span>
      <span class="text-kicker">Current</span>
      <span class="text-kicker text-text-accent">Proposed</span>
    </div>

    {#each FIELD_KEYS as field (field)}
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
  </section>

  <!-- Credits -->
  {#if credits.length > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Users class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Credits</span>
        <span class="font-mono text-[0.7rem] text-text-muted">
          {credits.filter((credit) => store.isReviewProposalSelected(credit.proposalId)).length} of {credits.length} selected
        </span>
      </header>
      <div class="grid grid-cols-1 gap-2 p-3.5 sm:grid-cols-2">
        {#each credits as credit (credit.proposalId)}
          {@const scopedCredit = scopedCreditForProposal(proposal, credit)}
          {@const image = preferredProposalImage(credit)}
          {@const card = {
            entity: { id: credit.proposalId, kind: "person", title: credit.patch?.title ?? "", parentEntityId: null, sortOrder: null, capabilities: [], childrenByKind: [], relationships: [] },
            aspectRatio: { width: 4, height: 5 },
            cover: image ? { src: image.url, alt: credit.patch?.title ?? "" } : null,
            hover: { kind: "none" } as const,
            subtitle: scopedCredit?.character ? `as ${scopedCredit.character}` : roleLabel(scopedCredit),
            meta: [{ icon: "person" as const, label: roleLabel(scopedCredit) }],
          }}
          <EntityThumbnail
            {card}
            layout="list"
            linkable={false}
            onActivate={() => walkChild(credit)}
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
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Layers class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Relationships</span>
        <span class="font-mono text-[0.7rem] text-text-muted">
          {nonCreditRelationships.filter((relationship) => store.isReviewProposalSelected(relationship.proposalId)).length} of {nonCreditRelationships.length} selected
        </span>
      </header>
      <div class="grid grid-cols-1 gap-2 p-3.5 sm:grid-cols-2">
        {#each nonCreditRelationships as relationship (relationship.proposalId)}
          <EntityThumbnail
            card={relationshipCard(relationship)}
            layout="list"
            linkable={false}
            onActivate={() => walkChild(relationship)}
            selectable
            selectMode
            selected={store.isReviewProposalSelected(relationship.proposalId)}
            onSelectedChange={(selected) => setRelationshipSelected(relationship, selected)}
          />
        {/each}
      </div>
    </section>
  {/if}

  <!-- Artwork -->
  {#if artworkCandidateCount > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Images class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Artwork</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{artworkCandidateCount} candidates</span>
      </header>
      <div class="grid grid-cols-1 gap-4 p-3.5 md:grid-cols-3">
        {#each imageGroups as group (group.kind)}
          <div>
            <div class="mb-2 flex items-center gap-2">
              <span class="text-kicker">{group.kind}</span>
              <span class="font-mono text-[0.62rem] text-text-disabled">{group.images.length}</span>
            </div>
            <div class="grid grid-cols-3 gap-1.5" class:grid-cols-2={group.kind === "backdrop"}>
              {#each group.images as image (image.url)}
                <button
                  type="button"
                  class={cn(
                    "relative overflow-hidden rounded-xs border transition-all",
                    selectedImages[group.kind] === image.url
                      ? "border-border-accent-strong shadow-[0_0_16px_rgba(242,194,106,0.2)]"
                      : "border-border-default hover:border-border-accent",
                  )}
                  style="aspect-ratio: {group.kind === 'poster' ? '2/3' : group.kind === 'backdrop' ? '16/9' : '2/1'};"
                  onclick={() => setImageSelected(group.kind, image.url)}
                >
                  <img src={image.url} alt="" class="h-full w-full object-cover" />
                  {#if selectedImages[group.kind] === image.url}
                    <div class="absolute right-1 top-1">
                      <span class="grid h-4 w-4 place-items-center rounded-xs bg-accent-500 text-[#0b0b0c]">
                        <Check class="h-2.5 w-2.5" />
                      </span>
                    </div>
                  {/if}
                  <div class="absolute bottom-0 left-0 right-0 flex justify-between bg-black/75 px-1 py-0.5">
                    <span class="font-mono text-[0.55rem] text-phosphor-600">{image.source}</span>
                    {#if image.width && image.height}
                      <span class="font-mono text-[0.55rem] text-text-disabled">{image.width}×{image.height}</span>
                    {/if}
                  </div>
                </button>
              {/each}
            </div>
          </div>
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

  <!-- Children -->
  {#if children.length > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Layers class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Children</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{selectedChildCount} of {children.length} selected</span>
      </header>
      <div class="grid grid-cols-1 gap-2 p-3.5 sm:grid-cols-2">
        {#each children as child, i (child.proposalId)}
          {@const childImage = preferredProposalImage(child)}
          {@const childCard = {
            entity: { id: child.proposalId, kind: child.targetKind, title: child.patch?.title ?? `Child ${i + 1}`, parentEntityId: null, sortOrder: i, capabilities: [], childrenByKind: [], relationships: [] },
            aspectRatio: "poster" as const,
            cover: childImage ? { src: childImage.url, alt: child.patch?.title ?? "" } : null,
            hover: { kind: "none" } as const,
            subtitle: child.targetKind,
            meta: child.confidence ? [{ icon: "count" as const, label: `${Math.round(child.confidence * 100)}%` }] : [],
          }}
          <EntityThumbnail
            card={childCard}
            layout="list"
            linkable={false}
            onActivate={() => walkChild(child)}
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
      class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-default bg-transparent px-3 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-2 hover:text-text-primary"
      onclick={() => void store.deleteQueueItem(entity.id)}
    >
      <X class="h-3.5 w-3.5" />
      Delete
    </button>
    <span class="font-mono text-[0.7rem] text-text-muted">
      {Object.values(selectedFields).filter(Boolean).length} fields
      · {Object.values(selectedImages).filter(Boolean).length} imgs
      · {selectedRelationshipCount} rels
      · {selectedTagCount} tags
      · {selectedChildCount} children
    </span>
    <div class="flex-1"></div>
    <button
      type="button"
      class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-3 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary"
      onclick={() => store.navigateToDashboard()}
    >
      <SkipForward class="h-3.5 w-3.5" />
      Skip
    </button>
    <button
      type="button"
      class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-accent-strong px-3 text-[0.78rem] text-text-primary transition-all disabled:cursor-not-allowed disabled:opacity-40"
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
        class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-accent-strong px-3 text-[0.78rem] text-text-primary transition-all disabled:cursor-not-allowed disabled:opacity-40"
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
