<script lang="ts">
  import { ChevronDown, Layers, Loader2 } from "@lucide/svelte";
  import { Button, Toggle, cn } from "@prismedia/ui-svelte";
  import { MONITOR_STATUS } from "$lib/api/generated/codes";
  import { fetchMonitors, stopMonitor } from "$lib/api/monitors";
  import { commitEntityRequest, commitReviewedRequest } from "$lib/api/requests";
  import type { RequestReviewResponse } from "$lib/api/generated/model";
  import { ApiError } from "$lib/api/orval-fetch";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import {
    buildSeasonPassRows,
    type SeasonPassRow,
  } from "$lib/requests/season-pass-options";
  import { MONITOR_PRESET_OPTIONS, resolvePresetSelection, type MonitorPresetChild } from "$lib/requests/monitor-presets";
  import type { MonitorPresetCode } from "$lib/api/generated/codes";

  /**
   * The Season Pass bulk monitoring editor for a series detail page. One row per season — season number,
   * episode count, and a monitor toggle — plus preset shortcut buttons that apply in bulk. Each toggle
   * requests local seasons by Entity id and provider-only seasons by revision-bound proposal id. Bulk
   * shortcuts run those same actions sequentially. Monitor state comes from one /api/monitors fetch,
   * indexed by the Entity id each monitor surfaces.
   */
  let {
    seasonCards,
    seasonEpisodeCounts,
    providerReview = null,
    seriesEntityId = null,
    hideNsfw = true,
    onChanged,
  }: {
    /** The series' local season thumbnail cards (entity id, title, season number in sortOrder). */
    seasonCards: EntityThumbnailCard[];
    /** Episode counts per local season entity id, when the page has them. */
    seasonEpisodeCounts: Record<string, number>;
    /** Canonical plugin proposal, including seasons not materialized locally yet. */
    providerReview?: RequestReviewResponse | null;
    /** The series entity id, used to preserve its current monitor preset when requesting provider-only seasons. */
    seriesEntityId?: string | null;
    /** Visibility ceiling used for both review and commit revalidation. */
    hideNsfw?: boolean;
    /** Refreshes the parent detail without remounting the whole page. */
    onChanged?: () => void | Promise<void>;
  } = $props();

  let open = $state(false);
  let loading = $state(false);
  let acting = $state(false);
  let error = $state<string | null>(null);
  /** Entity ids of seasons currently actively monitored (indexed from /api/monitors). */
  let monitoredIds = $state<Set<string>>(new Set());
  /** Provider-only season keys that were just requested but may not have local entity ids until the parent refresh completes. */
  let optimisticMonitoredKeys = $state<Set<string>>(new Set());
  let seriesMonitorPreset = $state<MonitorPresetCode | null>(null);

  // Seasons ordered by their number, reduced to the fields the rows and preset shortcuts need.
  const seasons = $derived(
    buildSeasonPassRows({
      localSeasons: seasonCards,
      episodeCounts: seasonEpisodeCounts,
      providerReview,
    }),
  );
  const presetChildren = $derived<MonitorPresetChild[]>(
    // Every row is requestable for preset purposes here — an already monitored season is simply left on.
    seasons.map((season) => ({ id: season.key, number: season.number, requestable: true })),
  );
  // The bulk preset shortcuts offered as buttons — the actionable subset (All/Future/First/Latest/None).
  const presetShortcuts = $derived(
    MONITOR_PRESET_OPTIONS.filter((option) =>
      (["all", "future", "first-season", "latest-season", "none"] as string[]).includes(option.value),
    ),
  );

  async function loadMonitors() {
    loading = true;
    error = null;
    try {
      const monitors = await fetchMonitors();
      const active = monitors.filter((monitor) => monitor.status === MONITOR_STATUS.active);
      monitoredIds = new Set(
        active
          .filter((monitor) => monitor.entityId)
          .map((monitor) => monitor.entityId as string),
      );
      seriesMonitorPreset = seriesEntityId
        ? (active.find((monitor) => monitor.entityId === seriesEntityId)?.preset as MonitorPresetCode | undefined) ?? null
        : null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load monitoring state";
    } finally {
      loading = false;
    }
  }

  function expand() {
    open = !open;
    if (open && !loading) void loadMonitors();
  }

  function durablyMonitored(season: SeasonPassRow): boolean {
    return !!season.entityId && monitoredIds.has(season.entityId);
  }

  function rowMonitored(season: SeasonPassRow): boolean {
    return optimisticMonitoredKeys.has(season.key) || durablyMonitored(season);
  }

  function optimisticallySetSeason(season: SeasonPassRow, monitored: boolean) {
    const next = new Set(optimisticMonitoredKeys);
    if (monitored && !season.entityId) {
      next.add(season.key);
    } else {
      next.delete(season.key);
    }
    optimisticMonitoredKeys = next;

    if (season.entityId) {
      const nextIds = new Set(monitoredIds);
      if (monitored) {
        nextIds.add(season.entityId);
      } else {
        nextIds.delete(season.entityId);
      }
      monitoredIds = nextIds;
    }
  }

  function reconcileOptimisticState() {
    if (optimisticMonitoredKeys.size === 0) return;
    const remaining = new Set<string>();
    for (const season of seasons) {
      if (optimisticMonitoredKeys.has(season.key) && !durablyMonitored(season)) {
        remaining.add(season.key);
      }
    }
    optimisticMonitoredKeys = remaining;
  }

  function actionError(error: unknown, fallback: string): string {
    if (error instanceof ApiError && error.status === 409) {
      return "The provider's season list changed. Refresh this series before trying again.";
    }
    return error instanceof Error ? error.message : fallback;
  }

  /** Monitor a season by requesting it (creates its acquisition + per-item monitor), or stop its monitor. */
  async function setSeasonMonitored(season: SeasonPassRow, monitored: boolean) {
    if (monitored) {
      if (season.entityId) {
        await commitEntityRequest(season.entityId);
        return;
      }

      if (!providerReview || !season.proposalId) {
        throw new Error("This provider season is missing its reviewed proposal identity.");
      }

      await commitReviewedRequest(
        {
          kind: providerReview.kind,
          pluginId: providerReview.pluginId,
          rootExternalIdentity: providerReview.externalIdentity,
          proposalRevision: providerReview.revision,
          selectedProposalIds: [season.proposalId],
          ...(seriesMonitorPreset ? { preset: seriesMonitorPreset } : {}),
        },
        hideNsfw,
      );
      return;
    }

    if (!season.entityId) return;

    // Stop every active monitor whose acquisition targets this season (usually one).
    const monitors = await fetchMonitors();
    const targets = monitors.filter(
      (monitor) => monitor.entityId === season.entityId && monitor.status === MONITOR_STATUS.active,
    );
    for (const monitor of targets) {
      await stopMonitor(monitor.id);
    }
  }

  async function toggleSeason(season: SeasonPassRow, monitored: boolean) {
    if (acting) return;
    acting = true;
    error = null;
    const previousOptimistic = new Set(optimisticMonitoredKeys);
    const previousMonitored = new Set(monitoredIds);
    try {
      optimisticallySetSeason(season, monitored);
      await setSeasonMonitored(season, monitored);
      await onChanged?.();
      await loadMonitors();
      reconcileOptimisticState();
    } catch (err) {
      optimisticMonitoredKeys = previousOptimistic;
      monitoredIds = previousMonitored;
      error = actionError(err, "Failed to update monitoring");
    } finally {
      acting = false;
    }
  }

  /** Apply a preset in bulk: the preset's derived seasons become monitored, the rest are unmonitored. */
  async function applyPreset(preset: MonitorPresetCode) {
    if (acting) return;
    acting = true;
    error = null;
    const previousOptimistic = new Set(optimisticMonitoredKeys);
    const previousMonitored = new Set(monitoredIds);
    try {
      const wanted = new Set(resolvePresetSelection(preset, presetChildren));
      // Sequentially — like WantedList's bulk actions — so a large series never floods the search queue.
      for (const season of seasons) {
        const shouldMonitor = wanted.has(season.key);
        const isMonitored = rowMonitored(season);
        if (shouldMonitor !== isMonitored) {
          optimisticallySetSeason(season, shouldMonitor);
          await setSeasonMonitored(season, shouldMonitor);
        }
      }
      await onChanged?.();
      await loadMonitors();
      reconcileOptimisticState();
    } catch (err) {
      optimisticMonitoredKeys = previousOptimistic;
      monitoredIds = previousMonitored;
      error = actionError(err, "Failed to apply preset");
    } finally {
      acting = false;
    }
  }
</script>

{#if seasons.length > 0}
  <section class="season-pass surface-panel">
    <button type="button" class="header" aria-expanded={open} onclick={expand}>
      <span class="header-title">
        <Layers class="h-4 w-4 text-text-accent" />
        Season Pass
        <span class="header-count">{seasons.length}</span>
      </span>
      <ChevronDown class={cn("h-4 w-4 text-text-muted transition-transform", open && "rotate-180")} />
    </button>

    {#if open}
      <div class="body">
        <div class="shortcuts">
          <span class="shortcuts-label">Monitor</span>
          {#each presetShortcuts as shortcut (shortcut.value)}
            <Button
              type="button"
              size="sm"
              variant="ghost"
              disabled={acting || loading}
              title={shortcut.description}
              onclick={() => void applyPreset(shortcut.value)}
            >
              {shortcut.label}
            </Button>
          {/each}
        </div>

        {#if error}
          <p class="error">{error}</p>
        {/if}

        {#if loading}
          <p class="loading"><Loader2 class="h-4 w-4 animate-spin" /> Loading monitoring state…</p>
        {:else}
          <ul class="rows">
            {#each seasons as season (season.key)}
              {@const monitored = rowMonitored(season)}
              <li class="row">
                <span class="row-number">Season {season.number}</span>
                {#if season.episodes !== null}
                  <span class="row-episodes">{season.episodes} {season.episodes === 1 ? "episode" : "episodes"}</span>
                {/if}
                <Toggle
                  checked={monitored}
                  disabled={acting}
                  ariaLabel={`Monitor season ${season.number}`}
                  onchange={(value) => void toggleSeason(season, value)}
                />
              </li>
            {/each}
          </ul>
        {/if}
      </div>
    {/if}
  </section>
{/if}

<style>
  .season-pass {
    display: flex;
    flex-direction: column;
  }

  .header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
    padding: 0.75rem 1rem;
    background: transparent;
    border: none;
    cursor: pointer;
    color: var(--color-text-primary);
  }

  .header-title {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    font-family: var(--font-heading);
    font-size: 0.9rem;
    font-weight: 600;
  }

  .header-count {
    font-family: var(--font-mono);
    font-size: 0.7rem;
    color: var(--color-text-muted);
  }

  .body {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
    padding: 0 1rem 1rem;
  }

  .shortcuts {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.4rem;
  }

  .shortcuts-label {
    font-family: var(--font-mono);
    font-size: 0.66rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--color-text-secondary);
    margin-right: 0.25rem;
  }

  .rows {
    display: flex;
    flex-direction: column;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    background: var(--color-surface-1);
  }

  .row {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 0.5rem 0.75rem;
    border-bottom: 1px solid var(--color-border-subtle);
  }

  .row:last-child {
    border-bottom: none;
  }

  .row-number {
    flex: 1;
    min-width: 0;
    font-size: 0.82rem;
    color: var(--color-text-primary);
  }

  .row-episodes {
    font-family: var(--font-mono);
    font-size: 0.7rem;
    color: var(--color-text-muted);
  }

  .error {
    font-size: 0.75rem;
    color: var(--color-error-text, #f87171);
  }

  .loading {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    font-size: 0.8rem;
    color: var(--color-text-muted);
  }
</style>
