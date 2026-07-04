<script lang="ts">
  import { ChevronDown, Layers, Loader2 } from "@lucide/svelte";
  import { Button, Toggle, cn } from "@prismedia/ui-svelte";
  import { MONITOR_STATUS } from "$lib/api/generated/codes";
  import { fetchMonitors, stopMonitor } from "$lib/api/monitors";
  import { commitEntityRequest } from "$lib/api/requests";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { MONITOR_PRESET_OPTIONS, resolvePresetSelection, type MonitorPresetChild } from "$lib/requests/monitor-presets";
  import type { MonitorPresetCode } from "$lib/api/generated/codes";

  /**
   * The Season Pass bulk monitoring editor for a series detail page. One row per season — season number,
   * episode count, and a monitor toggle — plus preset shortcut buttons that apply in bulk. Each toggle
   * drives the EXISTING per-entity endpoints for that season's entity (request-to-monitor on, stop-monitor
   * off), run sequentially like WantedList's bulk actions — no new backend bulk endpoint. Monitor state
   * comes from a single /api/monitors fetch, indexed by the entity id each monitor now surfaces.
   */
  let {
    seasonCards,
    seasonEpisodeCounts,
  }: {
    /** The series' season thumbnail cards (entity id, title, season number in sortOrder). */
    seasonCards: EntityThumbnailCard[];
    /** Episode counts per season entity id, when the page has them. */
    seasonEpisodeCounts: Record<string, number>;
  } = $props();

  let open = $state(false);
  let loading = $state(false);
  let acting = $state(false);
  let error = $state<string | null>(null);
  /** Entity ids of seasons currently actively monitored (indexed from /api/monitors). */
  let monitoredIds = $state<Set<string>>(new Set());

  // Seasons ordered by their number, reduced to the fields the rows and preset shortcuts need.
  const seasons = $derived(
    seasonCards
      .map((card) => ({
        id: card.entity.id,
        title: card.entity.title,
        number: typeof card.entity.sortOrder === "number" ? card.entity.sortOrder : null,
        episodes: seasonEpisodeCounts[card.entity.id] ?? null,
      }))
      .sort((a, b) => (a.number ?? Number.MAX_SAFE_INTEGER) - (b.number ?? Number.MAX_SAFE_INTEGER)),
  );
  const presetChildren = $derived<MonitorPresetChild[]>(
    // Every season is "requestable" for preset purposes here — a season already monitored is simply left on.
    seasons.map((season) => ({ id: season.id, number: season.number, requestable: true })),
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
      monitoredIds = new Set(
        monitors
          .filter((monitor) => monitor.status === MONITOR_STATUS.active && monitor.entityId)
          .map((monitor) => monitor.entityId as string),
      );
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load monitoring state";
    } finally {
      loading = false;
    }
  }

  function expand() {
    open = !open;
    if (open && monitoredIds.size === 0 && !loading) void loadMonitors();
  }

  /** Monitor a season by requesting it (creates its acquisition + per-item monitor), or stop its monitor. */
  async function setSeasonMonitored(seasonId: string, monitored: boolean) {
    if (monitored) {
      await commitEntityRequest(seasonId);
    } else {
      // Stop every active monitor whose acquisition targets this season (usually one).
      const monitors = await fetchMonitors();
      const targets = monitors.filter(
        (monitor) => monitor.entityId === seasonId && monitor.status === MONITOR_STATUS.active,
      );
      for (const monitor of targets) {
        await stopMonitor(monitor.id);
      }
    }
  }

  async function toggleSeason(seasonId: string, monitored: boolean) {
    if (acting) return;
    acting = true;
    error = null;
    try {
      await setSeasonMonitored(seasonId, monitored);
      await loadMonitors();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to update monitoring";
    } finally {
      acting = false;
    }
  }

  /** Apply a preset in bulk: the preset's derived seasons become monitored, the rest are unmonitored. */
  async function applyPreset(preset: MonitorPresetCode) {
    if (acting) return;
    acting = true;
    error = null;
    try {
      const wanted = new Set(resolvePresetSelection(preset, presetChildren));
      // Sequentially — like WantedList's bulk actions — so a large series never floods the search queue.
      for (const season of seasons) {
        const shouldMonitor = wanted.has(season.id);
        const isMonitored = monitoredIds.has(season.id);
        if (shouldMonitor !== isMonitored) {
          await setSeasonMonitored(season.id, shouldMonitor);
        }
      }
      await loadMonitors();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to apply preset";
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
            {#each seasons as season (season.id)}
              {@const monitored = monitoredIds.has(season.id)}
              <li class="row">
                <span class="row-number">{season.number !== null ? `Season ${season.number}` : season.title}</span>
                {#if season.episodes !== null}
                  <span class="row-episodes">{season.episodes} {season.episodes === 1 ? "episode" : "episodes"}</span>
                {/if}
                <Toggle
                  checked={monitored}
                  disabled={acting}
                  ariaLabel={`Monitor ${season.number !== null ? `season ${season.number}` : season.title}`}
                  onchange={(value) => void toggleSeason(season.id, value)}
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
