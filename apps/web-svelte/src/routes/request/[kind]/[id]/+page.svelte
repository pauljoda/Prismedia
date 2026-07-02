<script lang="ts">
  import { page } from "$app/state";
  import { BookOpen, ChevronLeft, ListMusic, Loader2, Send } from "@lucide/svelte";
  import { Button, dur, ease, flyUp } from "@prismedia/ui-svelte";
  import { fade } from "svelte/transition";
  import { goto } from "$app/navigation";
  import { commitRequest, fetchRequestDetail } from "$lib/api/requests";
  import { REQUEST_COMMIT_OUTCOME } from "$lib/api/generated/codes";
  import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import EntityDetail from "$lib/components/entities/EntityDetail.svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import RequestTargetOptions from "$lib/components/acquisitions/RequestTargetOptions.svelte";
  import SelectableCardSection from "$lib/components/review/SelectableCardSection.svelte";
  import type { EntityDetailActionButton } from "$lib/components/entities/entity-detail-types";
  import { requestDetailToEntityCard } from "$lib/requests/request-entity-card";
  import {
    requestCastToThumbnailCards,
    requestChildToThumbnailCard,
    requestStudiosToThumbnailCards,
  } from "$lib/requests/review-cards";
  import { formatDurationString } from "$lib/utils/format";
  import { inferRequestSourceForKind, requestKindInfo } from "$lib/requests/request-helpers";
  import type { RequestChildOption, RequestDetailResponse } from "$lib/requests/request-model";

  const params = $derived(page.params as { kind: RequestMediaKindCode; id: string });
  const sourceQuery = $derived(page.url.searchParams.get("source") as RequestProviderKindCode | null);
  /** Query string of the originating search page, chained through so Back returns to live results. */
  const backQuery = $derived(page.url.searchParams.get("back"));

  let detail = $state<RequestDetailResponse | null>(null);
  let selectedChildIds = $state<string[]>([]);
  /** Request-time choices: which library the files import into and which quality profile applies. */
  let targetLibraryRootId = $state<string | null>(null);
  let profileId = $state<string | null>(null);
  let loading = $state(true);
  let submitting = $state(false);
  let error = $state<string | null>(null);
  /** The child whose info preview popup is open (body-click on a card), null when closed. */
  let infoChild = $state<RequestChildOption | null>(null);

  const requestCard = $derived(detail ? requestDetailToEntityCard(detail) : null);
  // Per-kind flow hints from the shared kind catalog: containers (author, artist) select children and
  // fan them out one acquisition each; leaves request themselves; non-committable kinds only browse.
  const kindInfo = $derived(detail ? requestKindInfo(detail.kind) : null);
  const committable = $derived(kindInfo?.committable ?? false);
  const childNoun = $derived(kindInfo?.childNoun ?? "item");
  const children = $derived(detail?.children ?? []);
  const hasChildren = $derived(children.length > 0);
  const childCards = $derived(children.map(requestChildToThumbnailCard));
  const selectableChildIds = $derived(children.filter((child) => child.requestable).map((child) => child.id));

  const backHref = $derived(backQuery ? `/request?${backQuery}` : "/request");

  // Rich provider metadata rendered through the same shared blocks a real (identified) entity uses.
  const castCards = $derived(detail?.cast?.length ? requestCastToThumbnailCards(detail.cast) : []);
  const studioCards = $derived(detail?.studios?.length ? requestStudiosToThumbnailCards(detail.studios) : []);
  const tracks = $derived(detail?.tracks ?? []);

  function trackDuration(seconds: number | string | null | undefined): string | null {
    const value = typeof seconds === "string" ? Number(seconds) : seconds;
    if (!value || !Number.isFinite(value)) return null;
    const h = Math.floor(value / 3600);
    const m = Math.floor((value % 3600) / 60);
    const s = Math.floor(value % 60);
    return formatDurationString(`${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`);
  }

  // A standalone leaf (no children) requests straight from the hero; container kinds use the works footer.
  const actionButtons = $derived<EntityDetailActionButton[]>(
    detail && committable && !hasChildren
      ? [
          {
            id: "request",
            label: "Request",
            icon: Send,
            variant: "primary",
            onClick: () => void requestSelection(),
            disabled: submitting,
          },
        ]
      : [],
  );

  let loadedKey = $state("");
  $effect(() => {
    const key = `${params.kind}:${params.id}:${sourceQuery ?? ""}`;
    if (key === loadedKey) return;
    loadedKey = key;
    void initialize();
  });

  async function initialize() {
    loading = true;
    detail = null;
    error = null;
    selectedChildIds = [];
    targetLibraryRootId = null;
    profileId = null;
    infoChild = null;
    try {
      const resolvedSource = sourceQuery ?? inferRequestSourceForKind(params.kind);
      if (!resolvedSource) {
        throw new Error("This request kind is not supported.");
      }
      detail = await fetchRequestDetail({ source: resolvedSource, kind: params.kind, externalId: params.id });
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load request detail";
    } finally {
      loading = false;
    }
  }

  function toggleChild(id: string, selected: boolean) {
    selectedChildIds = selected
      ? Array.from(new Set([...selectedChildIds, id]))
      : selectedChildIds.filter((childId) => childId !== id);
  }

  function toggleAll(selectAll: boolean) {
    selectedChildIds = selectAll ? [...selectableChildIds] : [];
  }

  /**
   * Prismedia-direct request through the single server-side commit: the backend creates the wanted
   * library entity/entities from the plugin proposal (a container with its picked works, a standalone
   * leaf, or picked sibling volumes) and starts one acquisition per requested item — no client fan-out.
   */
  async function requestSelection() {
    if (!detail || !committable) return;
    const picked = hasChildren
      ? children.filter((child) => child.requestable && selectedChildIds.includes(child.id)).map((child) => child.id)
      : [];
    if (hasChildren && picked.length === 0) {
      error = `Select at least one ${childNoun} to request.`;
      return;
    }

    submitting = true;
    error = null;
    try {
      const response = await commitRequest({
        kind: detail.kind,
        externalId: detail.externalId,
        selectedChildIds: picked,
        targetLibraryRootId,
        profileId,
      });

      const items = response.items ?? [];
      const requested = items.filter((item) => item.outcome === REQUEST_COMMIT_OUTCOME.requested);
      const skipped = items.filter((item) => item.outcome !== REQUEST_COMMIT_OUTCOME.requested);
      if (requested.length === 0) {
        // Nothing new was started — everything picked is already owned or already in flight.
        const owned = skipped.filter((item) => item.outcome === REQUEST_COMMIT_OUTCOME.alreadyOwned).length;
        error = owned === skipped.length
          ? "Already in your library — nothing to request."
          : "Already requested — the existing requests are still searching.";
        return;
      }

      // Single new acquisition → its progress page; multiple → the request queue where all of them show.
      await goto(
        requested.length === 1 && requested[0].acquisitionId
          ? `/request/acquisition/${requested[0].acquisitionId}`
          : "/request",
      );
    } catch (err) {
      error = err instanceof Error ? err.message : "Request failed";
    } finally {
      submitting = false;
    }
  }

  function openInfo(cardId: string) {
    infoChild = children.find((child) => child.id === cardId) ?? null;
  }
</script>

<svelte:head><title>{detail?.title ?? "Request"} · Prismedia</title></svelte:head>

<div class="space-y-4">
  <a
    href={backHref}
    class="inline-flex items-center gap-1 text-[0.78rem] font-medium text-text-muted transition-colors hover:text-text-primary"
  >
    <ChevronLeft class="h-4 w-4" />
    Back to search
  </a>

  {#if loading}
    <EntityDetailSkeleton />
  {:else if error && !detail}
    <div class="surface-panel p-6 text-[0.82rem] text-error-text">{error}</div>
  {:else if detail && requestCard}
    {@const d = detail}
    <EntityDetail card={requestCard} {actionButtons} posterSize="medium">
      {#snippet afterBody()}
        {#if !committable}
          <p class="rounded-sm border border-border-subtle bg-surface-1 p-3 text-[0.78rem] leading-relaxed text-text-muted">
            Requesting {kindInfo?.plural.toLowerCase() ?? "this kind"} isn't available yet — its per-episode
            acquisition engine is on the roadmap. You can still browse the details here.
          </p>
        {:else if hasChildren}
          <div class="space-y-3">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <p class="text-[0.78rem] leading-relaxed text-text-muted">
                Select the {childNoun}s to request — Prismedia searches your indexers and downloads each one,
                then imports it into the library you choose below.
              </p>
              <Button
                type="button"
                variant="primary"
                class="shrink-0 gap-2"
                disabled={submitting || selectedChildIds.length === 0}
                title={selectedChildIds.length === 0 ? `Select ${childNoun}s to request` : undefined}
                onclick={() => void requestSelection()}
              >
                {#if submitting}
                  <Loader2 class="h-4 w-4 animate-spin" />
                {:else}
                  <Send class="h-4 w-4" />
                {/if}
                {submitting
                  ? "Requesting…"
                  : selectedChildIds.length === 0
                    ? "Request"
                    : `Request ${selectedChildIds.length} ${childNoun}${selectedChildIds.length === 1 ? "" : "s"}`}
              </Button>
            </div>

            {#if kindInfo}
              <RequestTargetOptions {kindInfo} bind:targetLibraryRootId bind:profileId />
            {/if}

            {#if error}
              <p class="text-[0.75rem] leading-relaxed text-error-text">{error}</p>
            {/if}

            <SelectableCardSection
              panelId="request-works"
              title={childNoun === "album" ? "Albums" : childNoun === "book" ? "Books" : "Volumes"}
              cards={childCards}
              selectedIds={selectedChildIds}
              selectableIds={selectableChildIds}
              onToggle={toggleChild}
              onToggleAll={toggleAll}
              onActivate={(card) => openInfo(card.entity.id)}
            >
              {#snippet icon()}<BookOpen class="h-3.5 w-3.5 text-text-accent" />{/snippet}
            </SelectableCardSection>
          </div>
        {:else}
          <!-- Standalone leaf: the hero's Request button commits; the same questions sit right below it. -->
          <div class="space-y-2 rounded-sm border border-border-subtle bg-surface-1 p-3">
            {#if kindInfo}
              <RequestTargetOptions {kindInfo} bind:targetLibraryRootId bind:profileId />
            {/if}
            {#if error}
              <p class="text-[0.75rem] leading-relaxed text-error-text">{error}</p>
            {/if}
          </div>
        {/if}

        <!-- ── Provider metadata, rendered like the identified entity it becomes ── -->
        {#if tracks.length > 0}
          <section class="space-y-2" aria-label="Tracks">
            <h3 class="flex items-center gap-1.5 font-mono text-[0.68rem] font-semibold uppercase tracking-[0.04em] text-text-secondary">
              <ListMusic class="h-3.5 w-3.5 text-text-muted" />
              Tracks
              <span class="font-normal text-text-muted">{tracks.length}</span>
            </h3>
            <ol class="divide-y divide-border-subtle rounded-sm border border-border-subtle bg-surface-1">
              {#each tracks as track (track.number + track.title)}
                <li class="flex items-baseline gap-3 px-3 py-1.5 text-[0.8rem]">
                  <span class="w-6 shrink-0 text-right font-mono text-[0.7rem] text-text-muted">{track.number}</span>
                  <span class="min-w-0 flex-1 truncate text-text-primary">{track.title}</span>
                  {#if trackDuration(track.durationSeconds)}
                    <span class="shrink-0 font-mono text-[0.7rem] text-text-muted">{trackDuration(track.durationSeconds)}</span>
                  {/if}
                </li>
              {/each}
            </ol>
          </section>
        {/if}

        {#if castCards.length > 0 || studioCards.length > 0}
          <EntityCastAndCrewSection creditCards={castCards} {studioCards} />
        {/if}
      {/snippet}
    </EntityDetail>
  {/if}
</div>

<!-- ── Book info preview (opened by clicking a work card; selection is preserved underneath) ── -->
{#if infoChild}
  {@const child = infoChild}
  {@const selected = selectedChildIds.includes(child.id)}
  <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
    <button
      type="button"
      class="absolute inset-0 bg-black/80 backdrop-blur-sm"
      aria-label="Close preview"
      onclick={() => (infoChild = null)}
      transition:fade={{ duration: dur.normal, easing: ease.enter }}
    ></button>

    <div
      role="dialog"
      aria-modal="true"
      aria-label={child.title}
      class="relative z-10 flex w-full max-w-lg gap-4 surface-elevated p-5"
      transition:flyUp
    >
      {#if child.posterUrl}
        <img
          src={child.posterUrl}
          alt={child.title}
          referrerpolicy="no-referrer"
          class="h-44 w-28 shrink-0 rounded-sm border border-border-subtle object-cover"
        />
      {/if}
      <div class="flex min-w-0 flex-1 flex-col">
        <h2 class="font-heading text-base font-semibold text-text-primary">{child.title}</h2>
        {#if child.year}<span class="mt-0.5 font-mono text-[0.7rem] text-text-muted">{child.year}</span>{/if}
        {#if child.overview}
          <p class="mt-2 max-h-40 overflow-y-auto text-[0.78rem] leading-relaxed text-text-secondary">
            {child.overview}
          </p>
        {/if}
        <div class="mt-auto flex justify-end gap-2 pt-3">
          <Button type="button" variant="ghost" onclick={() => (infoChild = null)}>Close</Button>
          {#if child.requestable}
            <Button
              type="button"
              variant={selected ? "secondary" : "primary"}
              onclick={() => toggleChild(child.id, !selected)}
            >
              {selected ? "Deselect" : "Select"}
            </Button>
          {/if}
        </div>
      </div>
    </div>
  </div>
{/if}
