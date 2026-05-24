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
    X,
  } from "@lucide/svelte";
  import { cn, StatusLed } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import {
    reviewChildProposals,
    structuralChildProposals,
    relationshipProposals,
  } from "$lib/components/identify-review";
  import type {
    EntityMetadataProposal,
    ImageCandidate,
  } from "$lib/api/identify";
  import type { EntityCard } from "$lib/api/prismedia";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useIdentifyStore } from "./identify-store.svelte";

  interface Props {
    entity: EntityCard;
    proposal: EntityMetadataProposal;
  }

  let { entity, proposal }: Props = $props();

  const store = useIdentifyStore();

  const FIELD_KEYS = [
    "title", "description", "externalIds", "urls", "tags",
    "studio", "credits", "dates", "stats", "positions", "classification",
  ] as const;

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
  };

  let selectedFields = $state<Record<string, boolean>>(
    Object.fromEntries(FIELD_KEYS.map((k) => [k, hasField(k)])),
  );
  let selectedImages = $state<Record<string, string | null>>(defaultImageSelection());

  const children = $derived(structuralChildProposals(proposal));
  const relationships = $derived(relationshipProposals(proposal));
  const credits = $derived(
    relationships.filter((r) => r.targetKind === "person"),
  );
  const tags = $derived(proposal.patch?.tags ?? []);
  const imageGroups = $derived(groupImages(proposal.images ?? []));

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
    return "";
  }

  function groupImages(images: ImageCandidate[]): Array<{ kind: string; images: ImageCandidate[] }> {
    const groups: Record<string, ImageCandidate[]> = {};
    for (const image of images) {
      groups[image.kind] = [...(groups[image.kind] ?? []), image];
    }
    return Object.entries(groups).map(([kind, imgs]) => ({ kind, images: imgs }));
  }

  function defaultImageSelection(): Record<string, string | null> {
    const selected: Record<string, string | null> = {};
    for (const group of groupImages(proposal.images ?? [])) {
      selected[group.kind] = group.images[0]?.url ?? null;
    }
    return selected;
  }

  function handleApply() {
    const fields = Object.entries(selectedFields)
      .filter(([, on]) => on)
      .map(([field]) => field);
    void store.applyProposal(entity, proposal, fields, selectedImages);
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
    {#if entity.coverUrl}
      <img src={entity.coverUrl} alt="" class="h-16 w-11 rounded-xs object-cover" />
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
        onclick={() => { selectedFields = Object.fromEntries(FIELD_KEYS.map((k) => [k, hasField(k)])); }}
      >
        All
      </button>
      <button
        type="button"
        class="text-[0.72rem] text-text-muted transition-colors hover:text-text-primary"
        onclick={() => { selectedFields = Object.fromEntries(FIELD_KEYS.map((k) => [k, false])); }}
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
        <div class="grid grid-cols-[auto_minmax(0,1fr)] items-start gap-3 border-b border-border-subtle px-3.5 py-3 last:border-b-0 md:grid-cols-[auto_110px_1fr_1fr]">
          <label class="flex items-center">
            <input
              type="checkbox"
              class="h-4 w-4 accent-accent-500"
              bind:checked={selectedFields[field]}
            />
          </label>
          <div class="md:contents">
            <div>
              <span class="font-heading text-[0.76rem] font-semibold text-text-secondary">{FIELD_LABELS[field]}</span>
              <span class="ml-2 font-mono text-[0.62rem] text-text-disabled md:ml-0 md:block">{field}</span>
            </div>
            <div class="hidden font-mono text-[0.74rem] text-text-disabled md:block">—</div>
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
        <span class="font-mono text-[0.7rem] text-text-muted">{credits.length} people</span>
      </header>
      <div class="grid grid-cols-1 gap-2 p-3.5 sm:grid-cols-2">
        {#each credits as credit (credit.proposalId)}
          {@const card = {
            entity: { id: credit.proposalId, kind: "person", title: credit.patch?.title ?? "", parentEntityId: null, sortOrder: null, capabilities: [], childrenByKind: [], relationships: [] },
            aspectRatio: { width: 4, height: 5 },
            cover: credit.images[0] ? { src: credit.images[0].url, alt: credit.patch?.title ?? "" } : null,
            hover: { kind: "none" } as const,
            subtitle: credit.patch?.credits[0]?.character ? `as ${credit.patch?.credits[0].character}` : credit.patch?.credits[0]?.role ?? "",
            meta: [{ icon: "person" as const, label: credit.patch?.credits[0]?.role ?? "Cast" }],
          }}
          <EntityThumbnail
            {card}
            layout="list"
          />
        {/each}
      </div>
    </section>
  {/if}

  <!-- Artwork -->
  {#if (proposal.images ?? []).length > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Images class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Artwork</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{proposal.images.length} candidates</span>
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
  {#if tags.length > 0}
    <div class="flex flex-wrap items-center gap-2 rounded-sm border border-border-subtle bg-gradient-to-b from-surface-2 to-surface-1 px-3.5 py-2.5">
      <span class="text-kicker text-text-accent">Tags</span>
      <span class="font-mono text-[0.66rem] text-text-muted">{tags.length}</span>
      <div class="mx-1 h-4 w-px bg-border-subtle"></div>
      {#each tags as tag (tag)}
        <span class="inline-flex items-center gap-1 rounded-xs border border-border-accent bg-accent-950/30 px-2 py-1 text-[0.76rem] text-text-primary">
          <Check class="h-2.5 w-2.5 text-text-accent" />
          {tag}
        </span>
      {/each}
    </div>
  {/if}

  <!-- Children -->
  {#if children.length > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Layers class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Children</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{children.length} returned</span>
        <div class="flex-1"></div>
        <button
          type="button"
          class="inline-flex h-7 items-center gap-1 rounded-xs border border-border-default bg-surface-2 px-2 text-[0.72rem] text-text-primary transition-colors hover:bg-surface-3"
        >
          <Check class="h-3 w-3" />
          Accept all
        </button>
      </header>
      <div class="grid grid-cols-2 gap-4 p-4 sm:grid-cols-3 lg:grid-cols-4">
        {#each children as child, i (child.proposalId)}
          {@const childCard = {
            entity: { id: child.proposalId, kind: child.targetKind, title: child.patch?.title ?? `Child ${i + 1}`, parentEntityId: null, sortOrder: i, capabilities: [], childrenByKind: [], relationships: [] },
            aspectRatio: "poster" as const,
            cover: child.images[0] ? { src: child.images[0].url, alt: child.patch?.title ?? "" } : null,
            hover: { kind: "none" } as const,
            subtitle: child.targetKind,
            meta: [],
          }}
          <div class="relative flex flex-col gap-2">
            <div class="absolute -top-2 right-2.5 z-10 flex gap-1">
              {#if child.confidence}
                <span class="rounded-xs border border-phosphor-600/20 bg-surface-3 px-1.5 py-0.5 font-mono text-[0.58rem] text-phosphor-600">
                  {Math.round(child.confidence * 100)}%
                </span>
              {/if}
            </div>
            <EntityThumbnail card={childCard} onActivate={() => walkChild(child)} />
            <div class="flex gap-1.5">
              <button
                type="button"
                class="inline-flex h-7 flex-1 items-center justify-center gap-1 rounded-xs border border-border-default bg-surface-2 text-[0.72rem] text-text-primary transition-colors hover:bg-surface-3"
              >
                Accept
              </button>
              <button
                type="button"
                class="inline-flex h-7 flex-1 items-center justify-center gap-1 rounded-xs border border-border-accent-strong bg-accent-950/40 text-[0.72rem] text-text-accent transition-colors hover:bg-accent-950/60"
                onclick={() => walkChild(child)}
              >
                Walk
                <ChevronRight class="h-3 w-3" />
              </button>
            </div>
          </div>
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
      · {credits.length} credits
      · {tags.length} tags
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
      onclick={handleApply}
    >
      {#if store.applying}
        <Loader2 class="h-4 w-4 animate-spin" />
      {:else}
        <Check class="h-4 w-4" />
      {/if}
      Apply
      {#if children.length > 0}
        & walk children
      {/if}
    </button>
  </div>
</div>
