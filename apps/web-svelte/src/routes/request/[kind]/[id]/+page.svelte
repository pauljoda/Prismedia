<script lang="ts">
  import { page } from "$app/state";
  import { BookOpen, ChevronLeft, Loader2, Send } from "@lucide/svelte";
  import { Button, dur, ease, flyUp } from "@prismedia/ui-svelte";
  import { fade } from "svelte/transition";
  import { goto } from "$app/navigation";
  import { fetchRequestDetail } from "$lib/api/requests";
  import { createAcquisition } from "$lib/api/acquisitions";
  import { REQUEST_MEDIA_KIND } from "$lib/api/generated/codes";
  import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import EntityDetail from "$lib/components/entities/EntityDetail.svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import SelectableCardSection from "$lib/components/review/SelectableCardSection.svelte";
  import type { EntityDetailActionButton } from "$lib/components/entities/entity-detail-types";
  import { requestDetailToEntityCard } from "$lib/requests/request-entity-card";
  import { requestChildToThumbnailCard } from "$lib/requests/review-cards";
  import { inferRequestSourceForKind, numericValue } from "$lib/requests/request-helpers";
  import type { RequestChildOption, RequestDetailResponse } from "$lib/requests/request-model";

  const params = $derived(page.params as { kind: RequestMediaKindCode; id: string });
  const sourceQuery = $derived(page.url.searchParams.get("source") as RequestProviderKindCode | null);
  /** Query string of the originating search page, chained through so Back returns to live results. */
  const backQuery = $derived(page.url.searchParams.get("back"));

  let detail = $state<RequestDetailResponse | null>(null);
  let selectedChildIds = $state<string[]>([]);
  let loading = $state(true);
  let submitting = $state(false);
  let error = $state<string | null>(null);
  /** The child whose info preview popup is open (body-click on a card), null when closed. */
  let infoChild = $state<RequestChildOption | null>(null);

  const requestCard = $derived(detail ? requestDetailToEntityCard(detail) : null);
  // An author/series is a container: its children (books/volumes) are selected and fanned out one each.
  const isAuthor = $derived(detail?.kind === REQUEST_MEDIA_KIND.author);
  const childNoun = $derived(isAuthor ? "book" : "volume");
  const children = $derived(detail?.children ?? []);
  const hasChildren = $derived(children.length > 0);
  const childCards = $derived(children.map(requestChildToThumbnailCard));
  const selectableChildIds = $derived(children.filter((child) => child.requestable).map((child) => child.id));

  const backHref = $derived(backQuery ? `/request?${backQuery}` : "/request");

  // A standalone book (no children) requests straight from the hero; container kinds use the works footer.
  const actionButtons = $derived<EntityDetailActionButton[]>(
    detail && !hasChildren
      ? [
          {
            id: "request",
            label: "Request",
            icon: Send,
            variant: "primary",
            onClick: () => void requestPluginBook(),
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
   * Prismedia-direct request: fans out one acquisition per selected child (an author's books or a series'
   * volumes), or a single acquisition for a standalone book, then moves to the searching view.
   */
  async function requestPluginBook() {
    if (!detail) return;
    const d = detail;
    // Targets: the selected children, or the standalone book itself.
    const raw = hasChildren
      ? children
          .filter((child) => child.requestable && selectedChildIds.includes(child.id))
          .map((child) => ({ id: child.id, title: child.title, year: child.year, posterUrl: child.posterUrl, overview: child.overview }))
      : [{ id: d.externalId, title: d.title, year: d.year, posterUrl: d.posterUrl, overview: d.overview }];
    if (raw.length === 0) {
      error = `Select at least one ${childNoun} to request.`;
      return;
    }

    // Pre-flight: every target must carry a resolvable provider-qualified id ("provider:itemId") so each
    // acquisition can identify ID-first. Validate up front so we never partially fan out on a bad id.
    const targets = raw.map((target) => {
      const separator = target.id.indexOf(":");
      return {
        ...target,
        pluginId: separator > 0 ? target.id.slice(0, separator) : null,
        pluginItemId: separator > 0 ? target.id.slice(separator + 1) : null,
      };
    });
    if (targets.some((target) => !target.pluginId || !target.pluginItemId)) {
      error = "Could not resolve a provider id for the selection.";
      return;
    }

    submitting = true;
    error = null;
    const created: string[] = [];
    try {
      for (const target of targets) {
        const summary = await createAcquisition({
          title: target.title,
          // For an author container the author IS the entity title; for a book/series it's the subtitle.
          author: isAuthor ? d.title : (d.subtitle ?? null),
          // Only a book-series names its children's series; an author's books are unrelated works.
          series: !isAuthor && hasChildren ? d.title : null,
          year: numericValue(target.year),
          posterUrl: target.posterUrl ?? null,
          pluginId: target.pluginId,
          pluginItemId: target.pluginItemId,
          // The target's own overview (the standalone target already carries d.overview); never the parent's,
          // so an author's bio can't leak in as a book description.
          description: target.overview ?? null,
        });
        created.push(summary.id);
      }

      // Single book → its acquisition page; multiple → the request queue where all of them show.
      await goto(created.length === 1 ? `/request/acquisition/${created[0]}` : "/request");
    } catch (err) {
      const reason = err instanceof Error ? err.message : "Request failed";
      if (created.length > 0) {
        // A mid-loop failure already queued some books. Drop those from the selection so pressing Request
        // again retries only the remainder (never re-creating the ones that succeeded), and say what happened.
        const queuedIds = new Set(targets.slice(0, created.length).map((target) => target.id));
        selectedChildIds = selectedChildIds.filter((id) => !queuedIds.has(id));
        error = `Requested ${created.length} of ${targets.length} ${childNoun}${targets.length === 1 ? "" : "s"} — the rest failed (${reason}). The queued ones are in your requests; press Request again to retry the remainder.`;
      } else {
        error = reason;
      }
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
        {#if hasChildren}
          <div class="space-y-3">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <p class="text-[0.78rem] leading-relaxed text-text-muted">
                Select the {childNoun}s to request — Prismedia searches your indexers and downloads each one,
                then imports it. Quality rules come from your default book profile (Settings → Acquisition).
              </p>
              <Button
                type="button"
                variant="primary"
                class="shrink-0 gap-2"
                disabled={submitting || selectedChildIds.length === 0}
                title={selectedChildIds.length === 0 ? `Select ${childNoun}s to request` : undefined}
                onclick={() => void requestPluginBook()}
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

            {#if error}
              <p class="text-[0.75rem] leading-relaxed text-error-text">{error}</p>
            {/if}

            <SelectableCardSection
              panelId="request-works"
              title={isAuthor ? "Books" : "Volumes"}
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
        {:else if error}
          <p class="text-[0.75rem] leading-relaxed text-error-text">{error}</p>
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
