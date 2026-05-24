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
    Users,
  } from "@lucide/svelte";
  import { cn, StatusLed } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import {
    defaultImageSelectionForReview,
    reviewableImages,
    reviewFieldKeys,
    structuralChildProposals,
    relationshipProposals,
    scopedCreditForProposal,
  } from "$lib/components/identify-review";
  import type {
    CreditPatch,
    EntityMetadataProposal,
    ImageCandidate,
  } from "$lib/api/identify";
  import type { EntityCard } from "$lib/api/prismedia";
  import { useIdentifyStore } from "./identify-store.svelte";

  interface Props {
    entity: EntityCard;
    proposal: EntityMetadataProposal;
    parentProposal: EntityMetadataProposal;
    ancestors?: EntityMetadataProposal[];
  }

  let { entity, proposal, parentProposal, ancestors = [parentProposal] }: Props = $props();

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
  let reviewStateProposalId = $state<string | null>(null);

  const children = $derived(structuralChildProposals(proposal));
  const credits = $derived(relationshipProposals(proposal).filter((r) => r.targetKind === "person"));
  const imageGroups = $derived(groupImages(reviewableImages(proposal.images ?? [])));
  const artworkCandidateCount = $derived(imageGroups.reduce((count, group) => count + group.images.length, 0));

  const parentChildren = $derived(structuralChildProposals(parentProposal));
  const currentIndex = $derived(parentChildren.findIndex((c) => c.proposalId === proposal.proposalId));
  const prevChild = $derived(currentIndex > 0 ? parentChildren[currentIndex - 1] : null);
  const nextChild = $derived(currentIndex < parentChildren.length - 1 ? parentChildren[currentIndex + 1] : null);

  $effect(() => {
    if (reviewStateProposalId === proposal.proposalId) return;
    reviewStateProposalId = proposal.proposalId;
    selectedFields = Object.fromEntries(FIELD_KEYS.map((k) => [k, hasField(k)]));
    selectedImages = defaultImageSelectionForReview(proposal);
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

  function preferredProposalImage(result: EntityMetadataProposal): ImageCandidate | null {
    const images = reviewableImages(result.images ?? []);
    return images.find((image) => image.kind === "poster") ??
      images.find((image) => image.kind === "thumbnail") ??
      images[0] ??
      null;
  }

  function roleLabel(credit: CreditPatch | null | undefined): string {
    const role = credit?.role?.trim();
    if (!role) return "Cast";
    return role.replaceAll("-", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
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
  <div class="grid grid-cols-[1fr_auto_auto] items-center gap-4 rounded-sm border border-border-subtle bg-surface-1 p-3.5 shadow-well">
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
        {Object.values(selectedFields).filter(Boolean).length}/{FIELD_KEYS.filter((k) => hasField(k)).length} accepted
      </span>
    </header>

    <div class="hidden grid-cols-[auto_110px_1fr_1fr] items-center gap-3 border-b border-border-default bg-surface-2 px-3.5 py-1.5 md:grid">
      <span class="w-5"></span>
      <span class="text-kicker">Field</span>
      <span class="text-kicker">Current</span>
      <span class="text-kicker text-text-accent">Proposed</span>
    </div>

    {#each FIELD_KEYS as field (field)}
      {#if hasField(field)}
        <div class="grid grid-cols-[auto_minmax(0,1fr)] items-start gap-3 border-b border-border-subtle px-3.5 py-3 last:border-b-0 md:grid-cols-[auto_110px_1fr_1fr]">
          <label class="flex items-center">
            <input type="checkbox" class="h-4 w-4 accent-accent-500" bind:checked={selectedFields[field]} />
          </label>
          <div class="md:contents">
            <div>
              <span class="font-heading text-[0.76rem] font-semibold text-text-secondary">{FIELD_LABELS[field]}</span>
            </div>
            <div class="hidden font-mono text-[0.74rem] text-text-disabled md:block">—</div>
            <div class="mt-1 text-[0.82rem] leading-snug text-text-primary md:mt-0">{fieldValue(field)}</div>
          </div>
        </div>
      {/if}
    {/each}
  </section>

  <!-- Credits (inherited) -->
  {#if credits.length > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Users class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Credits</span>
        <span class="font-mono text-[0.7rem] text-text-muted">inherited · {credits.length}</span>
      </header>
      <div class="flex flex-col gap-2 p-3.5">
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
          <EntityThumbnail {card} layout="list" linkable={false} onActivate={() => goToChild(credit)} />
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
                    "relative overflow-hidden rounded-xs border transition-all",
                    selectedImages[group.kind] === image.url
                      ? "border-border-accent-strong shadow-[0_0_16px_rgba(242,194,106,0.2)]"
                      : "border-border-default hover:border-border-accent",
                  )}
                  style="aspect-ratio: {group.kind === 'poster' ? '2/3' : '16/9'};"
                  onclick={() => (selectedImages[group.kind] = image.url)}
                >
                  <img src={image.url} alt="" class="h-full w-full object-cover" />
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
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Layers class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Children</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{children.length} matched</span>
        <div class="flex-1"></div>
        <button
          type="button"
          class="inline-flex h-7 items-center gap-1 rounded-xs border border-border-default bg-surface-2 px-2 text-[0.72rem] text-text-primary transition-colors hover:bg-surface-3"
        >
          <Check class="h-3 w-3" />
          Accept all
        </button>
      </header>
      <div class="grid grid-cols-2 gap-3 p-4 sm:grid-cols-3 lg:grid-cols-4">
        {#each children as child, i (child.proposalId)}
          {@const childCard = {
            entity: { id: child.proposalId, kind: child.targetKind, title: child.patch?.title ?? `Episode ${i + 1}`, parentEntityId: null, sortOrder: i, capabilities: [], childrenByKind: [], relationships: [] },
            aspectRatio: "video" as const,
            cover: child.images[0] ? { src: child.images[0].url, alt: child.patch?.title ?? "" } : null,
            hover: { kind: "none" } as const,
            subtitle: child.targetKind,
            custom: child.patch?.positions?.episode ? { bottomLeft: { label: `E${String(child.patch?.positions.episode).padStart(2, "0")}` } } : undefined,
            meta: [],
          }}
          <EntityThumbnail card={childCard} linkable={false} onActivate={() => goToChild(child)} />
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
    <div class="flex-1"></div>
    <button
      type="button"
      class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-3 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3"
      onclick={() => nextChild ? goToSibling(nextChild) : goBackToParent()}
    >
      <SkipForward class="h-3.5 w-3.5" />
      Skip
    </button>
    <button
      type="button"
      class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-accent-strong px-3 text-[0.78rem] text-text-primary transition-all"
      style="background: linear-gradient(135deg, rgba(242,194,106,0.24), rgba(242,194,106,0.1)); box-shadow: 0 0 18px rgba(242,194,106,0.16);"
      onclick={() => nextChild ? goToSibling(nextChild) : goBackToParent()}
    >
      <Check class="h-4 w-4" />
      Accept {proposal.patch?.title ?? ""}
    </button>
  </div>
</div>
