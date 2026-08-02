<script lang="ts">
  import { onMount } from "svelte";
  import {
    Activity,
    BookOpen,
    CalendarRange,
    Clock3,
    Flame,
    Gauge,
    History,
    Headphones,
    Layers,
    Loader2,
    Timer,
    Triangle,
    Trophy,
    UsersRound,
  } from "@lucide/svelte";
  import { Button, Select, cn, type SelectOption } from "@prismedia/ui-svelte";
  import ActivityTimeline from "$lib/components/stats/ActivityTimeline.svelte";
  import PrismDispersion from "$lib/components/stats/PrismDispersion.svelte";
  import RecentEventTimeline from "$lib/components/stats/RecentEventTimeline.svelte";
  import RhythmGrid from "$lib/components/stats/RhythmGrid.svelte";
  import StatFigure from "$lib/components/stats/StatFigure.svelte";
  import TopEntityBoard from "$lib/components/stats/TopEntityBoard.svelte";
  import { fetchEntityThumbnails } from "$lib/api/entities";
  import {
    fetchPlaybackStatistics,
    type PlaybackStatisticsParams,
  } from "$lib/api/playback-statistics";
  import { fetchUsers } from "$lib/api/users";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import {
    entityReferenceToThumbnailCard,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-thumbnail";
  import {
    ENTITY_KIND,
    CONSUMPTION_EVENT_KIND,
    resolveEntityHref,
    type EntityKindCode,
    type ConsumptionEventKindCode,
  } from "$lib/entities/entity-codes";
  import {
    buildDailySeries,
    buildDispersion,
    buildRhythm,
    completionRate,
    formatDayKey,
    formatDayShort,
    formatActiveDuration,
    localUtcOffsetMinutes,
    statNumber,
    summarizeCadence,
  } from "$lib/stats/playback-stats";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import type {
    PlaybackStatisticsEntity,
    PlaybackStatisticsEvent,
    PlaybackStatisticsResponse,
    UserResponse,
  } from "$lib/api/generated/model";
  import { useSession } from "$lib/stores/session.svelte";

  const ALL_FILTER = "all" as const;
  const ALL_USERS_SCOPE = "all-users" as const;

  type TimeframeKey = "30d" | "90d" | "year" | "all";
  type KindFilter = typeof ALL_FILTER | EntityKindCode;
  type EventFilter = typeof ALL_FILTER | ConsumptionEventKindCode;

  interface TimeframeOption {
    key: TimeframeKey;
    label: string;
    days: number | null;
  }

  const TIMEFRAMES: TimeframeOption[] = [
    { key: "30d", label: "30D", days: 30 },
    { key: "90d", label: "90D", days: 90 },
    { key: "year", label: "Year", days: 365 },
    { key: "all", label: "All", days: null },
  ];

  const EVENT_FILTERS: ReadonlyArray<{ value: EventFilter; label: string }> = [
    { value: ALL_FILTER, label: "All" },
    { value: CONSUMPTION_EVENT_KIND.accessed, label: "Opened" },
    { value: CONSUMPTION_EVENT_KIND.completed, label: "Completed" },
    { value: CONSUMPTION_EVENT_KIND.skipped, label: "Skips" },
  ];

  const nsfw = useNsfw();
  const session = useSession();

  let timeframe = $state<TimeframeKey>("year");
  let kindFilter = $state<KindFilter>(ALL_FILTER);
  let eventFilter = $state<EventFilter>(ALL_FILTER);
  let selectedScope = $state(session.user?.id ?? "");
  let users = $state.raw<UserResponse[]>([]);
  let scopeError = $state<string | null>(null);
  let stats = $state<PlaybackStatisticsResponse | null>(null);
  let thumbnailCardsById = $state.raw<Map<string, EntityThumbnailCard>>(new Map());
  let loading = $state(true);
  let error = $state<string | null>(null);
  let activeRequest = 0;
  let selectedDate = $state<string | null>(null);

  // The window's day, weekday, and hour folds happen server side, so the viewer's offset travels
  // with the query instead of being reconstructed from UTC buckets in the browser.
  const utcOffsetMinutes = localUtcOffsetMinutes();

  const totalEvents = $derived(statNumber(stats?.totalEvents));
  const accessedCount = $derived(statNumber(stats?.accessedCount));
  const completedCount = $derived(statNumber(stats?.completedCount));
  const skippedCount = $derived(statNumber(stats?.skippedCount));
  const distinctEntityCount = $derived(statNumber(stats?.distinctEntityCount));
  const activeSeconds = $derived(statNumber(stats?.activeSeconds));
  const viewingSeconds = $derived(statNumber(stats?.viewingSeconds));
  const readingSeconds = $derived(statNumber(stats?.readingSeconds));
  const listeningSeconds = $derived(statNumber(stats?.listeningSeconds));

  const dispersionBands = $derived(buildDispersion(stats?.kindBreakdown ?? []));
  const rhythm = $derived(buildRhythm(stats?.rhythm ?? []));
  const dailySeries = $derived(
    stats ? buildDailySeries(stats.dailyEvents, stats.from, stats.to, utcOffsetMinutes) : [],
  );
  const cadence = $derived(summarizeCadence(dailySeries));
  const completion = $derived(completionRate(completedCount, skippedCount));

  const topEntities = $derived(stats?.topEntities ?? []);
  const recentEvents = $derived(stats?.recentEvents ?? []);

  const showAccessed = $derived(
    eventFilter === ALL_FILTER || eventFilter === CONSUMPTION_EVENT_KIND.accessed,
  );
  const showCompleted = $derived(
    eventFilter === ALL_FILTER || eventFilter === CONSUMPTION_EVENT_KIND.completed,
  );
  const showSkipped = $derived(
    eventFilter === ALL_FILTER || eventFilter === CONSUMPTION_EVENT_KIND.skipped,
  );
  const showEmpty = $derived(!loading && !error && totalEvents === 0 && activeSeconds === 0);

  const windowLabel = $derived.by(() => {
    if (!stats || dailySeries.length === 0) return "";
    const first = dailySeries[0].date;
    const last = dailySeries[dailySeries.length - 1].date;
    // A one-year window has the same month and day at both ends, so the year has to be shown or
    // the label collapses into an identical-looking pair.
    const options: Intl.DateTimeFormatOptions =
      first.slice(0, 4) === last.slice(0, 4)
        ? { month: "short", day: "numeric" }
        : { month: "short", day: "numeric", year: "numeric" };
    return `${formatDayKey(first, options)} – ${formatDayKey(last, options)}`;
  });

  const scopeOptions = $derived.by<SelectOption[]>(() => {
    if (!session.isAdmin) return [];
    const availableUsers = users.length > 0 ? users : session.user ? [session.user] : [];
    return [
      { value: ALL_USERS_SCOPE, label: "All users" },
      ...availableUsers.map((user) => ({
        value: user.id,
        label: `${user.displayName}${user.id === session.user?.id ? " (you)" : ""}`,
      })),
    ];
  });

  onMount(() => {
    if (!session.isAdmin) return;
    void fetchUsers()
      .then((items) => {
        users = items;
        scopeError = null;
      })
      .catch((err) => {
        scopeError = err instanceof Error ? err.message : "Failed to load consumption scopes";
      });
  });

  $effect(() => {
    const params = buildQuery(
      timeframe,
      kindFilter,
      eventFilter,
      nsfw.mode === "off",
      selectedScope,
      session.isAdmin,
    );
    const requestId = ++activeRequest;
    const controller = new AbortController();

    loading = true;
    error = null;

    loadStatistics(params, nsfw.mode === "off", controller.signal)
      .then(({ response, thumbnails }) => {
        if (requestId !== activeRequest) return;
        stats = response;
        thumbnailCardsById = thumbnails;
      })
      .catch((err) => {
        if (requestId !== activeRequest || isAbortError(err)) return;
        stats = null;
        thumbnailCardsById = new Map();
        error = err instanceof Error ? err.message : "Failed to load consumption statistics";
      })
      .finally(() => {
        if (requestId === activeRequest) loading = false;
      });

    return () => controller.abort();
  });

  async function loadStatistics(
    params: PlaybackStatisticsParams,
    hideNsfw: boolean,
    signal: AbortSignal,
  ): Promise<{ response: PlaybackStatisticsResponse; thumbnails: Map<string, EntityThumbnailCard> }> {
    const response = await fetchPlaybackStatistics(params, { signal });
    const thumbnails = await fetchEntityThumbnails(entityIdsForStatistics(response), { hideNsfw, signal });
    return {
      response,
      thumbnails: new Map(
        thumbnails.map((thumbnail) => [
          thumbnail.id,
          entityCardToThumbnailCard(thumbnail, resolveEntityHref(thumbnail.kind, thumbnail.id)),
        ]),
      ),
    };
  }

  function buildQuery(
    selectedTimeframe: TimeframeKey,
    selectedKind: KindFilter,
    selectedEvent: EventFilter,
    hideNsfw: boolean,
    scope: string,
    isAdmin: boolean,
  ): PlaybackStatisticsParams {
    const to = new Date();
    const from = fromForTimeframe(selectedTimeframe, to);
    const query: PlaybackStatisticsParams = {
      from: from.toISOString(),
      to: to.toISOString(),
      kind: selectedKind === ALL_FILTER ? undefined : selectedKind,
      eventKind: selectedEvent === ALL_FILTER ? undefined : selectedEvent,
      hideNsfw,
      utcOffsetMinutes,
    };
    if (isAdmin && scope === ALL_USERS_SCOPE) {
      query.allUsers = true;
    } else if (isAdmin && scope) {
      query.userId = scope;
    }
    return query;
  }

  function fromForTimeframe(selectedTimeframe: TimeframeKey, to: Date): Date {
    const option = TIMEFRAMES.find((item) => item.key === selectedTimeframe);
    if (!option || option.days == null) return new Date("1970-01-01T00:00:00.000Z");

    return new Date(to.getTime() - option.days * 24 * 60 * 60 * 1000);
  }

  function isAbortError(err: unknown): boolean {
    return err instanceof DOMException && err.name === "AbortError";
  }

  function entityIdsForStatistics(response: PlaybackStatisticsResponse): string[] {
    return [
      ...new Set([
        ...response.topEntities.map((entity) => entity.id),
        ...response.recentEvents.map((event) => event.entityId),
      ]),
    ];
  }

  function topEntityThumbnail(entity: PlaybackStatisticsEntity): EntityThumbnailCard {
    return (
      thumbnailCardsById.get(entity.id) ??
      entityReferenceToThumbnailCard({
        id: entity.id,
        kind: entity.kind,
        title: entity.title,
        thumbnailUrl: entity.coverUrl,
      })
    );
  }

  function recentEventThumbnail(event: PlaybackStatisticsEvent): EntityThumbnailCard {
    return (
      thumbnailCardsById.get(event.entityId) ??
      entityReferenceToThumbnailCard({
        id: event.entityId,
        kind: event.entityKind,
        title: event.entityTitle,
        thumbnailUrl: event.coverUrl,
      })
    );
  }

  function selectTimeframe(value: TimeframeKey) {
    timeframe = value;
    selectedDate = null;
  }

  function selectEvent(value: EventFilter) {
    eventFilter = value;
    selectedDate = null;
  }

  function selectKind(value: string | null) {
    kindFilter = (value as EntityKindCode | null) ?? ALL_FILTER;
    selectedDate = null;
  }

  function selectScope(value: string) {
    selectedScope = value;
    selectedDate = null;
  }

  function opensPerActiveDayLabel(): string {
    if (cadence.activeDays === 0) return "No activity yet";
    return `${(accessedCount / cadence.activeDays).toFixed(1)} per active day`;
  }
</script>

<svelte:head>
  <title>Consumption Stats · Prismedia</title>
</svelte:head>

<section class="space-y-3 pb-6">
  <header class="stats-head">
    <div class="min-w-0">
      <h1 class="stats-title">
        <Activity class="h-5 w-5 text-text-muted" aria-hidden="true" />
        Consumption Stats
      </h1>
      <p class="stats-subtitle">
        {#if loading && !stats}
          Reading consumption history
        {:else if windowLabel}
          {windowLabel} · {totalEvents.toLocaleString()} events across {cadence.activeDays.toLocaleString()} active
          {cadence.activeDays === 1 ? "day" : "days"}
        {:else}
          No consumption history in this window
        {/if}
      </p>
    </div>

    <div class="stats-controls">
      {#if session.isAdmin}
        <div class="surface-well flex min-w-44 items-center gap-1 p-0.5 pl-2">
          <UsersRound class="h-3.5 w-3.5 shrink-0 text-text-muted" aria-hidden="true" />
          <Select
            size="sm"
            class="min-w-36 border-0 bg-transparent shadow-none"
            value={selectedScope}
            options={scopeOptions}
            ariaLabel="Consumption statistics user scope"
            onchange={selectScope}
          />
        </div>
      {/if}

      <div class="surface-well flex w-fit max-w-full flex-wrap gap-1 p-0.5">
        {#each TIMEFRAMES as option (option.key)}
          <Button
            variant={timeframe === option.key ? "primary" : "ghost"}
            size="sm"
            class="h-6 px-2 text-[0.7rem]"
            onclick={() => selectTimeframe(option.key)}
          >
            {option.label}
          </Button>
        {/each}
      </div>

      <div class="surface-well flex w-fit max-w-full flex-wrap gap-1 p-0.5">
        {#each EVENT_FILTERS as option (option.value)}
          <Button
            variant={eventFilter === option.value ? "primary" : "ghost"}
            size="sm"
            class="h-6 px-2 text-[0.7rem]"
            onclick={() => selectEvent(option.value)}
          >
            {option.label}
          </Button>
        {/each}
      </div>
    </div>
  </header>

  {#if error}
    <div class="surface-panel border-l-2 border-error px-3 py-2 text-sm text-error-text" role="alert">
      {error}
    </div>
  {/if}

  {#if scopeError}
    <div class="surface-panel border-l-2 border-warning px-3 py-2 text-sm text-warning-text" role="status">
      {scopeError} — showing your activity.
    </div>
  {/if}

  {#if loading && !stats}
    <div class="surface-panel flex min-h-72 items-center justify-center">
      <Loader2 class="h-5 w-5 animate-spin text-accent-300" aria-hidden="true" />
      <span class="sr-only">Loading consumption statistics</span>
    </div>
  {:else if showEmpty}
    <div class="surface-panel flex min-h-72 flex-col items-center justify-center px-4 text-center">
      <History class="h-6 w-6 text-text-muted" aria-hidden="true" />
      <h2 class="mt-2 font-heading text-base text-text-primary">No consumption history yet</h2>
      <p class="mt-1 max-w-md text-sm text-text-muted">
        Opens, completions, skips, and active time appear here as you watch, listen, and read. Adjust the
        timeframe or filters above if you expected to see something.
      </p>
      {#if kindFilter !== ALL_FILTER || eventFilter !== ALL_FILTER}
        <Button
          variant="ghost"
          size="sm"
          class="mt-3"
          onclick={() => {
            selectKind(null);
            selectEvent(ALL_FILTER);
          }}
        >
          Clear filters
        </Button>
      {/if}
    </div>
  {:else}
    <!--
      The dispersion is this page's single accent moment: one library of consumption entering the
      prism and separating into its media families. Everything below it stays neutral.
    -->
    <section class={cn("surface-panel overflow-hidden", loading && "stats-refreshing")}>
      <div class="panel-head">
        <div>
          <h2 class="panel-title">
            <Triangle class="h-3.5 w-3.5 text-text-muted" aria-hidden="true" />
            Spectrum
          </h2>
          <p class="panel-subtitle">
            {dispersionBands.length === 1
              ? "Filtered to one media family"
              : `Consumption separated across ${dispersionBands.length} media families`} · select a
            family to filter the page
          </p>
        </div>
        {#if kindFilter !== ALL_FILTER}
          <Button variant="ghost" size="sm" class="h-6 px-2 text-[0.7rem]" onclick={() => selectKind(null)}>
            Clear family
          </Button>
        {/if}
      </div>
      <div class="px-3 py-3 sm:px-4">
        <PrismDispersion bands={dispersionBands} activeKind={kindFilter === ALL_FILTER ? null : kindFilter} onSelect={selectKind} />
      </div>
    </section>

    <section class={cn("surface-panel overflow-hidden", loading && "stats-refreshing")}>
      <div class="stats-figures">
        <StatFigure
          label="Activity time"
          value={formatActiveDuration(activeSeconds)}
          hint={cadence.activeDays > 0
            ? `${formatActiveDuration(cadence.activeSecondsPerActiveDay)} per active day`
            : undefined}
          icon={Timer}
          emphasis
        />
        {#if viewingSeconds > 0}
          <StatFigure
            label="Watching"
            value={formatActiveDuration(viewingSeconds)}
            icon={Clock3}
          />
        {/if}
        {#if readingSeconds > 0}
          <StatFigure
            label="Reading"
            value={formatActiveDuration(readingSeconds)}
            icon={BookOpen}
          />
        {/if}
        {#if listeningSeconds > 0}
          <StatFigure
            label="Audiobooks"
            value={formatActiveDuration(listeningSeconds)}
            icon={Headphones}
          />
        {/if}
        <StatFigure
          label="Opened"
          value={accessedCount.toLocaleString()}
          hint={opensPerActiveDayLabel()}
          icon={Activity}
        />
        <StatFigure
          label="Completion"
          value={`${Math.round(completion * 100)}%`}
          hint={`${completedCount.toLocaleString()} completed · ${skippedCount.toLocaleString()} skipped`}
          icon={Gauge}
          ratio={completion}
        />
        <StatFigure
          label="Items reached"
          value={distinctEntityCount.toLocaleString()}
          hint="Distinct library items"
          icon={Layers}
        />
        <StatFigure
          label="Streak"
          value={`${cadence.currentStreak.toLocaleString()}d`}
          hint={`Longest ${cadence.longestStreak.toLocaleString()}d · ${cadence.activeDays.toLocaleString()} of ${cadence.totalDays.toLocaleString()} days active`}
          icon={Flame}
          ratio={cadence.totalDays > 0 ? cadence.activeDays / cadence.totalDays : null}
        />
        <StatFigure
          label="Busiest day"
          value={cadence.busiestDay ? formatDayShort(cadence.busiestDay.date) : "—"}
          hint={cadence.busiestDay
            ? `${cadence.busiestDay.totalEvents.toLocaleString()} events · ${formatActiveDuration(cadence.busiestDay.activeSeconds)}`
            : "No activity yet"}
          icon={CalendarRange}
        />
      </div>
    </section>

    <section class={cn("surface-panel overflow-hidden", loading && "stats-refreshing")}>
      <div class="panel-head">
        <div>
          <h2 class="panel-title">Activity</h2>
          <p class="panel-subtitle">Events per day across the selected window</p>
        </div>
      </div>
      <div class="px-3 py-3 sm:px-4">
        <ActivityTimeline
          series={dailySeries}
          {showAccessed}
          {showCompleted}
          {showSkipped}
          {selectedDate}
          onSelect={(date) => (selectedDate = date)}
        />
      </div>
    </section>

    <div class="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <section class={cn("surface-panel overflow-hidden", loading && "stats-refreshing")}>
        <div class="panel-head">
          <div>
            <h2 class="panel-title">
              <Clock3 class="h-3.5 w-3.5 text-text-muted" aria-hidden="true" />
              Rhythm
            </h2>
            <p class="panel-subtitle">Which hours of the week the library gets used</p>
          </div>
        </div>
        <div class="px-3 py-3 sm:px-4">
          <RhythmGrid {rhythm} />
        </div>
      </section>

      <section class={cn("surface-panel overflow-hidden", loading && "stats-refreshing")}>
        <div class="panel-head">
          <div>
            <h2 class="panel-title">
              <Trophy class="h-3.5 w-3.5 text-text-muted" aria-hidden="true" />
              Most active
            </h2>
            <p class="panel-subtitle">Ranked by opens, outcomes, and active time in this window</p>
          </div>
        </div>
        <TopEntityBoard entities={topEntities} thumbnailFor={topEntityThumbnail} />
      </section>
    </div>

    <section class={cn("surface-panel overflow-hidden", loading && "stats-refreshing")}>
      <div class="panel-head">
        <div>
          <h2 class="panel-title">
            <History class="h-3.5 w-3.5 text-text-muted" aria-hidden="true" />
            History
          </h2>
          <p class="panel-subtitle">The most recent consumption events</p>
        </div>
      </div>
      <RecentEventTimeline
        events={recentEvents}
        thumbnailFor={recentEventThumbnail}
        {utcOffsetMinutes}
      />
    </section>
  {/if}
</section>

<style>
  .stats-head {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
    padding-bottom: 0.5rem;
    border-bottom: 1px solid var(--color-border-subtle);
  }

  @media (min-width: 64rem) {
    .stats-head {
      flex-direction: row;
      align-items: flex-end;
      justify-content: space-between;
      gap: 1rem;
    }
  }

  .stats-title {
    display: inline-flex;
    align-items: center;
    gap: 0.6rem;
    margin: 0;
    font-family: var(--font-heading);
    font-size: 1.55rem;
    font-weight: 600;
    letter-spacing: -0.025em;
    line-height: 1.05;
  }

  .stats-subtitle {
    margin: 0.35rem 0 0;
    font-family: var(--font-mono);
    font-size: 0.68rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-text-muted);
  }

  .stats-controls {
    display: flex;
    flex-wrap: wrap;
    gap: 0.375rem;
  }

  @media (min-width: 64rem) {
    .stats-controls {
      justify-content: flex-end;
    }
  }

  .panel-head {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 0.75rem;
    padding: 0.6rem 0.75rem;
    border-bottom: 1px solid var(--color-border-subtle);
  }

  @media (min-width: 40rem) {
    .panel-head {
      padding-inline: 1rem;
    }
  }

  .panel-title {
    display: inline-flex;
    align-items: center;
    gap: 0.45rem;
    margin: 0;
    font-family: var(--font-heading);
    font-size: 0.95rem;
    font-weight: 600;
    letter-spacing: -0.01em;
    color: var(--color-text-primary);
  }

  .panel-subtitle {
    margin: 0.15rem 0 0;
    font-size: 0.72rem;
    color: var(--color-text-muted);
  }

  .stats-figures {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .stats-figures > :global(*) {
    border-top: 1px solid var(--color-border-subtle);
    border-left: 1px solid var(--color-border-subtle);
  }

  .stats-figures > :global(:nth-child(-n + 2)) {
    border-top: none;
  }

  .stats-figures > :global(:nth-child(2n + 1)) {
    border-left: none;
  }

  @media (min-width: 48rem) {
    .stats-figures {
      grid-template-columns: repeat(3, minmax(0, 1fr));
    }

    .stats-figures > :global(:nth-child(-n + 3)) {
      border-top: none;
    }

    .stats-figures > :global(:nth-child(2n + 1)) {
      border-left: 1px solid var(--color-border-subtle);
    }

    .stats-figures > :global(:nth-child(3n + 1)) {
      border-left: none;
    }
  }

  @media (min-width: 80rem) {
    .stats-figures {
      grid-template-columns: repeat(6, minmax(0, 1fr));
    }

    .stats-figures > :global(*) {
      border-top: none;
      border-left: 1px solid var(--color-border-subtle);
    }

    .stats-figures > :global(:first-child) {
      border-left: none;
    }
  }

  /* A refresh keeps the previous window on screen at reduced contrast instead of blanking it. */
  .stats-refreshing {
    opacity: 0.6;
    transition: opacity var(--duration-normal, 200ms) var(--ease-default, ease);
  }

  @media (prefers-reduced-motion: reduce) {
    .stats-refreshing {
      transition: none;
    }
  }
</style>
