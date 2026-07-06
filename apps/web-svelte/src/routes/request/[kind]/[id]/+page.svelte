<script lang="ts">
  import { page } from "$app/state";
  import { BookOpen, ChevronLeft, ListMusic, Loader2, Send } from "@lucide/svelte";
  import { Button, Select } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import { commitRequest, fetchRequestDetail } from "$lib/api/requests";
  import { REQUEST_COMMIT_OUTCOME } from "$lib/api/generated/codes";
  import type { MonitorPresetCode, RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import {
    DEFAULT_MONITOR_PRESET,
    MONITOR_PRESET_CUSTOM,
    MONITOR_PRESET_OPTIONS,
    presetForSelection,
    resolvePresetSelection,
    type MonitorPresetChild,
    type MonitorPresetSelectValue,
  } from "$lib/requests/monitor-presets";
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
  import { entityKindForRequest, inferRequestSourceForKind, requestKindInfo } from "$lib/requests/request-helpers";
  import { resolveEntityHref } from "$lib/entities/entity-codes";
  import type { RequestDetailResponse } from "$lib/requests/request-model";

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

  // ── Monitoring preset (container kinds only) ──
  // The preset Select drives the pre-checked child selection; a manual checkbox edit flips the Select to
  // the UI-only "Custom" state, and choosing a preset again snaps the checkboxes back. The chosen preset
  // rides along with the commit so it is recorded on the container monitor (governing future syncs).
  const presetChildren = $derived<MonitorPresetChild[]>(
    // The generated `number` is `number | string | null` (orval emits int + string for the pattern); coerce.
    children.map((child) => ({ id: child.id, number: toNumberOrNull(child.number), requestable: child.requestable })),
  );

  function toNumberOrNull(value: number | string | null | undefined): number | null {
    const parsed = typeof value === "string" ? Number(value) : value;
    return typeof parsed === "number" && Number.isFinite(parsed) ? parsed : null;
  }
  /** The last real preset chosen (never "Custom"): what the commit sends even after manual edits. */
  let chosenPreset = $state<MonitorPresetCode>(DEFAULT_MONITOR_PRESET);
  /** The Select's displayed value — the chosen preset, or "Custom" when the checkboxes diverge from it. */
  const presetDisplay = $derived<MonitorPresetSelectValue>(
    hasChildren ? presetForSelection(presetChildren, selectedChildIds) : chosenPreset,
  );
  const presetOptions = $derived([
    ...MONITOR_PRESET_OPTIONS.map((option) => ({ value: option.value, label: option.label })),
    // "Custom" is only ever a display state; it is disabled so the user cannot pick it directly.
    ...(presetDisplay === MONITOR_PRESET_CUSTOM ? [{ value: MONITOR_PRESET_CUSTOM, label: "Custom", disabled: true }] : []),
  ]);

  function applyPreset(value: string) {
    if (value === MONITOR_PRESET_CUSTOM) return;
    chosenPreset = value as MonitorPresetCode;
    selectedChildIds = resolvePresetSelection(chosenPreset, presetChildren);
  }

  // Seed the pre-checked selection from the default preset once children are known (once per load).
  let seededPreset = $state(false);
  $effect(() => {
    if (!seededPreset && hasChildren) {
      seededPreset = true;
      selectedChildIds = resolvePresetSelection(chosenPreset, presetChildren);
    }
  });

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

  // The Request action lives with the request controls (library/profile/works), never detached in
  // the hero — so the hero carries no action buttons on this page.
  const actionButtons: EntityDetailActionButton[] = [];

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
    seededPreset = false;
    chosenPreset = DEFAULT_MONITOR_PRESET;
    targetLibraryRootId = null;
    profileId = null;
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
        // The chosen preset (never "Custom") is recorded on the container monitor; for containers only.
        preset: hasChildren ? chosenPreset : null,
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

      // Single new acquisition → the wanted entity's own page, where its acquisition card shows the
      // live progress; multiple → the request hub's Downloads view where all of them show.
      const single = requested.length === 1 ? requested[0] : null;
      const requestedChild = single ? children.find((child) => child.id === single.externalId) : null;
      const singleHref = single?.entityId
        ? resolveEntityHref(entityKindForRequest(requestedChild?.kind ?? detail.kind), single.entityId)
        : null;
      await goto(singleHref ?? "/request");
    } catch (err) {
      error = err instanceof Error ? err.message : "Request failed";
    } finally {
      submitting = false;
    }
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
        <!-- Consistent breathing room between the request card, tracks, and cast/studio rails. -->
        <div class="space-y-5">
        {#if !committable}
          <p class="rounded-sm border border-border-subtle bg-surface-1 p-3 text-[0.78rem] leading-relaxed text-text-muted">
            Requesting {kindInfo?.plural.toLowerCase() ?? "this kind"} isn't available yet — its per-episode
            acquisition engine is on the roadmap. You can still browse the details here.
          </p>
        {:else if hasChildren}
          <div class="space-y-3">
            <!-- One request card owns the whole decision: profile, library, and the Request action. -->
            <div class="space-y-3 rounded-sm border border-border-accent bg-surface-1 p-4">
              <h3 class="flex items-center gap-1.5 font-mono text-[0.68rem] font-semibold uppercase tracking-[0.04em] text-text-secondary">
                <Send class="h-3.5 w-3.5 text-text-accent" />
                Request {childNoun}s
              </h3>
              <p class="text-[0.78rem] leading-relaxed text-text-muted">
                Select the {childNoun}s below — Prismedia searches your indexers and downloads each one,
                then imports it into the library you choose here.
              </p>
              <!-- Monitoring preset: drives the pre-checked selection and records the discovery scope. -->
              <label class="flex flex-col gap-1">
                <span class="font-mono text-[0.66rem] font-semibold uppercase tracking-[0.04em] text-text-secondary">Monitor</span>
                <Select
                  options={presetOptions}
                  value={presetDisplay}
                  size="sm"
                  onchange={applyPreset}
                />
              </label>
              {#if kindInfo}
                <RequestTargetOptions {kindInfo} bind:targetLibraryRootId bind:profileId>
                  {#snippet actions()}
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
                  {/snippet}
                </RequestTargetOptions>
              {/if}
              {#if error}
                <p class="text-[0.75rem] leading-relaxed text-error-text">{error}</p>
              {/if}
            </div>

            <SelectableCardSection
              panelId="request-works"
              title={`${childNoun.charAt(0).toUpperCase()}${childNoun.slice(1)}s`}
              cards={childCards}
              selectedIds={selectedChildIds}
              selectableIds={selectableChildIds}
              onToggle={toggleChild}
              onToggleAll={toggleAll}
            >
              {#snippet icon()}<BookOpen class="h-3.5 w-3.5 text-text-accent" />{/snippet}
            </SelectableCardSection>
          </div>
        {:else}
          <!-- Standalone leaf: one request card owns the whole decision — profile, library, and the
               Request action together, never a detached hero button. -->
          <div class="space-y-3 rounded-sm border border-border-accent bg-surface-1 p-4">
            <h3 class="flex items-center gap-1.5 font-mono text-[0.68rem] font-semibold uppercase tracking-[0.04em] text-text-secondary">
              <Send class="h-3.5 w-3.5 text-text-accent" />
              Request this {kindInfo?.label.toLowerCase() ?? "item"}
            </h3>
            {#if kindInfo}
              <RequestTargetOptions {kindInfo} bind:targetLibraryRootId bind:profileId>
                {#snippet actions()}
                  <Button
                    type="button"
                    variant="primary"
                    class="gap-2"
                    disabled={submitting}
                    onclick={() => void requestSelection()}
                  >
                    {#if submitting}
                      <Loader2 class="h-4 w-4 animate-spin" />
                    {:else}
                      <Send class="h-4 w-4" />
                    {/if}
                    {submitting ? "Requesting…" : "Request"}
                  </Button>
                {/snippet}
              </RequestTargetOptions>
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
        </div>
      {/snippet}
    </EntityDetail>
  {/if}
</div>
