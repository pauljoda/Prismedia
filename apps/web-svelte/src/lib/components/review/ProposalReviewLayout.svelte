<script module lang="ts">
  import { fetchPluginProviders, type PluginProvider } from "$lib/api/plugins";

  /** One provider list per page load: the preview only needs names and icons. */
  let providersRequest: Promise<PluginProvider[]> | null = null;
  function loadProviders(): Promise<PluginProvider[]> {
    providersRequest ??= fetchPluginProviders().catch(() => {
      // A failed load is not remembered, so the next review tries again.
      providersRequest = null;
      return [];
    });
    return providersRequest;
  }
</script>

<script lang="ts">
  import type { Snippet } from "svelte";
  import { Check, Loader2 } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import type { EntityDetailCard } from "$lib/api/entities";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { toAspectRatioNumeric } from "$lib/entities/entity-thumbnail";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import {
    groupReviewImages,
    isNewRelationshipTitle,
    relationshipProposals,
    relationshipTitlesForDetail,
    reviewImagePreviewUrl,
  } from "$lib/components/identify-review";
  import {
    creditCard,
    relationshipCard,
    tagRelationshipForTitle,
  } from "$lib/components/identify/identify-review-helpers";
  import { ENTITY_KIND, MEDIA_IMAGE_KIND, labelForEntityKind } from "$lib/entities/entity-codes";
  import { bandGradient, mediaFamilyForKind } from "$lib/entities/media-families";
  import ReviewDetailsList from "./ReviewDetailsList.svelte";

  interface Props {
    proposal: EntityMetadataProposal;
    title: string;
    subtitle?: string | null;
    posterUrl?: string | null;
    /** Artwork the entity has today, shown beside the proposed artwork when identifying. */
    currentPosterUrl?: string | null;
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
    statusLabel?: (proposal: EntityMetadataProposal) => string | null;
    /** Seasons, episodes, or tracks the proposal carries. */
    structure?: Snippet;
    /** The decision beside the preview: Accept and Reject, or the request options. */
    sidebar?: Snippet;
    disabled?: boolean;
  }

  let {
    proposal,
    title,
    subtitle = null,
    posterUrl = null,
    currentPosterUrl = null,
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
    statusLabel = () => null,
    structure,
    sidebar,
    disabled = false,
  }: Props = $props();

  /** Artwork that stands for the item itself, and artwork shaped like a frame. */
  const PORTRAIT_ART: ReadonlySet<string> = new Set([MEDIA_IMAGE_KIND.poster, MEDIA_IMAGE_KIND.cover]);
  const WIDE_ART: ReadonlySet<string> = new Set([MEDIA_IMAGE_KIND.backdrop, MEDIA_IMAGE_KIND.thumbnail, MEDIA_IMAGE_KIND.still]);

  let providers = $state<PluginProvider[]>([]);
  $effect(() => {
    void loadProviders().then((list) => (providers = list));
  });

  /** Year and first credit, when the caller has no better subtitle than an identifier. */
  const derivedSubtitle = $derived.by(() => {
    const patch = proposal.patch;
    const firstDate = patch.dateEntries?.[0]?.value ?? Object.values(patch.dates ?? {})[0] ?? "";
    const year = /^\d{4}/.exec(firstDate)?.[0] ?? null;
    const credit = patch.credits?.[0]?.name ?? null;
    return [year, credit].filter(Boolean).join(" · ") || null;
  });
  const shownSubtitle = $derived(subtitle ?? derivedSubtitle);

  const provider = $derived(providers.find((candidate) => candidate.id === proposal.provider) ?? null);
  const family = $derived(mediaFamilyForKind(proposal.targetKind));
  const confidence = $derived(
    typeof proposal.confidence === "number" && Number.isFinite(proposal.confidence) ? Math.round(proposal.confidence * 100) : null,
  );
  const relationships = $derived(relationshipProposals(proposal));
  const credits = $derived(relationships.filter((relationship) => relationship.targetKind === ENTITY_KIND.person));
  const related = $derived(relationships.filter((relationship) => relationship.targetKind !== ENTITY_KIND.person));
  const tags = $derived([...new Set(proposal.patch?.tags ?? [])]);
  const looseTags = $derived(tags.filter((tag) => !tagRelationshipForTitle(tag, relationships)));
  const existingTagTitles = $derived(relationshipTitlesForDetail(detail, ENTITY_KIND.tag));
  const imageGroups = $derived(groupReviewImages(proposal));
  const imageSelectionStore = {
    getReviewImageSelections: (proposalId: string) => imageSelectionsForProposal(proposalId),
  };

  /** The preview follows the artwork picked below, so the choice is visible where the decision is made. */
  const previewUrl = $derived.by(() => {
    for (const group of imageGroups) {
      if (!PORTRAIT_ART.has(group.kind)) continue;
      const picked = group.images.find((image) => image.url === selectedImages[group.kind]);
      if (picked) return reviewImagePreviewUrl(picked, proposal.targetKind);
    }
    return posterUrl;
  });
  const previewAspect = $derived(imageShape === "square" ? "1 / 1" : imageShape === "wide" ? "16 / 9" : "2 / 3");

  const sections = $derived(
    [
      { id: "details", label: "Details", count: null },
      credits.length > 0 ? { id: "people", label: "People", count: credits.length } : null,
      related.length > 0 ? { id: "related", label: "Related", count: related.length } : null,
      imageGroups.length > 0 ? { id: "artwork", label: "Artwork", count: imageGroups.reduce((sum, group) => sum + group.images.length, 0) } : null,
      looseTags.length > 0 ? { id: "tags", label: "Tags", count: looseTags.length } : null,
      structure ? { id: "contents", label: "Contents", count: null } : null,
    ].filter((section): section is { id: string; label: string; count: number | null } => section !== null),
  );

  function jump(id: string) {
    document.getElementById(`review-${id}-${proposal.proposalId}`)?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  function artworkAspect(kind: string): string {
    if (PORTRAIT_ART.has(kind)) return imageShape === "square" ? "1 / 1" : "2 / 3";
    if (WIDE_ART.has(kind)) return "16 / 9";
    return "2 / 1";
  }

  function kindLabel(kind: string): string {
    return kind.charAt(0).toUpperCase() + kind.slice(1);
  }
</script>

<div class="grid min-w-0 items-start gap-6 lg:grid-cols-[17rem_minmax(0,1fr)]">
  <!-- The proposal as it will look, and the decision, kept in view while the details scroll. -->
  <aside class="flex min-w-0 flex-col gap-4 lg:sticky lg:top-20" aria-label="Proposal">
    <div class="overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] shadow-[var(--shadow-card)]">
      <div class="flex gap-3 p-3 lg:flex-col">
        <div class="relative w-24 shrink-0 lg:w-full" style:aspect-ratio={previewAspect}>
          {#if previewUrl}
            <img src={previewUrl} alt="" class="size-full rounded-[var(--radius-sm)] object-cover" decoding="async" referrerpolicy="no-referrer" />
          {:else}
            <span class="block size-full rounded-[var(--radius-sm)] bg-[var(--color-surface-3)]"></span>
          {/if}

        </div>
        <div class="flex min-w-0 flex-1 flex-col gap-2 lg:pt-2">
          <div class="min-w-0">
            <h2 class="font-heading text-lg font-semibold leading-tight text-text-primary">{title}</h2>
            {#if shownSubtitle}<p class="truncate text-caption text-text-muted">{shownSubtitle}</p>{/if}
          </div>
          <p class="flex items-center gap-2 text-caption text-text-secondary">
            <span class="h-1.5 w-5 rounded-[2px]" style:background={bandGradient(family.accent)} aria-hidden="true"></span>
            {labelForEntityKind(proposal.targetKind)}
          </p>
          {#if currentPosterUrl && currentPosterUrl !== previewUrl}
            <p class="flex items-center gap-2 text-caption text-text-muted" title="Artwork in your library today">
              <img src={currentPosterUrl} alt="" class="h-8 w-auto rounded-[var(--radius-xs)] object-cover opacity-80" decoding="async" />
              Current artwork
            </p>
          {/if}
          <div class="flex items-center gap-2">
            <PluginIcon name={provider?.name ?? proposal.provider} iconUrl={provider?.iconUrl} class="size-6" />
            <span class="min-w-0 flex-1 truncate text-caption text-text-secondary">{provider?.name ?? proposal.provider}</span>
          </div>
          {#if confidence !== null}
            <div class="flex items-center gap-2" title="{confidence}% match">
              <span class="h-1 flex-1 overflow-hidden rounded-[2px] bg-[var(--color-surface-3)]">
                <span class="block h-full bg-[var(--color-text-secondary)]" style:width="{confidence}%"></span>
              </span>
              <span class="font-mono text-[0.68rem] tabular-nums text-text-secondary">{confidence}%</span>
            </div>
          {/if}
        </div>
      </div>
    </div>

    {#if sidebar}{@render sidebar()}{/if}
  </aside>

  <fieldset {disabled} class="flex min-w-0 flex-col gap-4 border-0 p-0">
    {#if sections.length > 1}
      <nav class="flex gap-1 overflow-x-auto border-b border-[var(--color-border-subtle)] pb-1.5 scrollbar-hidden" aria-label="Review sections">
        {#each sections as section (section.id)}
          <Button variant="ghost" size="sm" class="shrink-0 text-text-secondary first:-ml-2.5" onclick={() => jump(section.id)}>
            {section.label}
            {#if section.count !== null}<span class="font-mono text-caption text-text-muted">{section.count}</span>{/if}
          </Button>
        {/each}
      </nav>
    {/if}

    <div id="review-details-{proposal.proposalId}" class="scroll-mt-20">
      <ReviewDetailsList {proposal} {selectedFields} {currentValue} {onFieldChange} {onAllFields} />
    </div>

    {#snippet cardStrip(entries: EntityMetadataProposal[], id: string, label: string)}
      <section id="review-{id}-{proposal.proposalId}" class="flex min-w-0 scroll-mt-20 flex-col gap-2" aria-label={label}>
        <h3 class="font-heading text-sm font-semibold text-text-primary">
          {label}
          <span class="ml-1 font-mono text-caption font-normal text-text-muted">
            {entries.filter((entry) => isProposalSelected(entry.proposalId)).length}/{entries.length}
          </span>
        </h3>
        <ul class="flex gap-2.5 overflow-x-auto pb-2 scrollbar-hidden">
          {#each entries as entry (entry.proposalId)}
            {@const status = statusLabel(entry)}
            {@const card = id === "people"
              ? creditCard(entry, proposal, relationshipTitlesForDetail(detail, entry.targetKind), selectedImages, proposal.proposalId, imageSelectionStore)
              : relationshipCard(entry, relationshipTitlesForDetail(detail, entry.targetKind), selectedImages, proposal.proposalId, imageSelectionStore)}
            <!-- Wide cards (studios, episodes) take episode width; portrait cards stay narrow. -->
            <li class={cn("relative shrink-0", toAspectRatioNumeric(card.aspectRatio) > 1.2 ? "w-44" : "w-28")}>
              <EntityThumbnail
                {card}
                linkable={false}
                onActivate={onActivate ? () => onActivate?.(entry) : undefined}
                selectable
                selectMode
                selected={isProposalSelected(entry.proposalId)}
                onSelectedChange={(selected) => onProposalSelected(entry, selected)}
              />
              {#if status}
                <span class="absolute top-1.5 left-1.5 z-10 inline-flex items-center gap-1 rounded-[var(--radius-xs)] bg-black/80 px-1.5 py-0.5 font-mono text-[0.6rem] text-text-secondary">
                  <Loader2 class="size-3 animate-spin motion-reduce:animate-none" aria-hidden="true" />{status}
                </span>
              {/if}
            </li>
          {/each}
        </ul>
      </section>
    {/snippet}

    {#if credits.length > 0}{@render cardStrip(credits, "people", "People")}{/if}
    {#if related.length > 0}{@render cardStrip(related, "related", "Related")}{/if}

    {#if imageGroups.length > 0}
      <section id="review-artwork-{proposal.proposalId}" class="flex min-w-0 scroll-mt-20 flex-col gap-4" aria-label="Artwork">
        {#each imageGroups as group (group.kind)}
          <div class="flex min-w-0 flex-col gap-2">
            <h3 class="font-heading text-sm font-semibold text-text-primary">
              {kindLabel(group.kind)} <span class="ml-1 font-mono text-caption font-normal text-text-muted">{group.images.length}</span>
            </h3>
            <ul class="flex gap-2.5 overflow-x-auto pb-2 scrollbar-hidden">
              {#each group.images as image (image.url)}
                {@const picked = selectedImages[group.kind] === image.url}
                <li class="shrink-0" class:w-28={artworkAspect(group.kind) !== "16 / 9"} class:w-56={artworkAspect(group.kind) === "16 / 9"}>
                  <Button
                    variant="ghost"
                    class={cn(
                      "relative block h-auto w-full overflow-hidden rounded-[var(--radius-sm)] border p-0",
                      picked ? "border-[var(--color-text-primary)]" : "border-[var(--color-border-subtle)] opacity-80 hover:opacity-100",
                    )}
                    style="aspect-ratio: {artworkAspect(group.kind)}"
                    aria-pressed={picked}
                    aria-label="{picked ? 'Deselect' : 'Select'} {group.kind} from {image.source}"
                    title="{image.source}{image.width && image.height ? ` · ${image.width}×${image.height}` : ''}"
                    onclick={() => onImageChange(group.kind, picked ? null : image.url)}
                  >
                    <img src={reviewImagePreviewUrl(image, proposal.targetKind)} alt="" class="size-full object-cover" loading="lazy" decoding="async" referrerpolicy="no-referrer" />
                    {#if picked}
                      <span class="absolute top-1.5 right-1.5 grid size-5 place-items-center rounded-full bg-[var(--color-text-primary)] text-[var(--color-surface-1)]">
                        <Check class="size-3" aria-hidden="true" />
                      </span>
                    {/if}
                  </Button>
                </li>
              {/each}
            </ul>
          </div>
        {/each}
      </section>
    {/if}

    {#if looseTags.length > 0}
      <section id="review-tags-{proposal.proposalId}" class="flex min-w-0 scroll-mt-20 flex-col gap-2" aria-label="Tags">
        <h3 class="font-heading text-sm font-semibold text-text-primary">
          Tags <span class="ml-1 font-mono text-caption font-normal text-text-muted">{looseTags.filter((tag) => selectedTags[tag]).length}/{looseTags.length}</span>
        </h3>
        <ul class="flex flex-wrap gap-1.5">
          {#each looseTags as tag (tag)}
            {@const isNew = isNewRelationshipTitle(tag, existingTagTitles)}
            <li>
              <Button
                variant="ghost"
                size="sm"
                class={cn(
                  "h-auto gap-1.5 rounded-[var(--radius-sm)] border px-2.5 py-1 font-normal",
                  selectedTags[tag]
                    ? "border-[var(--color-border-default)] bg-[var(--color-surface-3)] text-text-primary"
                    : "border-dashed border-[var(--color-border-subtle)] text-text-muted",
                )}
                aria-pressed={selectedTags[tag]}
                onclick={() => onTagChange(tag, !selectedTags[tag])}
              >
                {tag}
                {#if isNew && detail}<span class="font-mono text-[0.6rem] uppercase tracking-[0.1em] text-text-muted">new</span>{/if}
              </Button>
            </li>
          {/each}
        </ul>
      </section>
    {/if}

    {#if structure}
      <div id="review-contents-{proposal.proposalId}" class="scroll-mt-20">{@render structure()}</div>
    {/if}
  </fieldset>
</div>
