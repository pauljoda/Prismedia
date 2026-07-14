<script lang="ts">
  import {
    Activity,
    ChartNoAxesCombined,
    Clock3,
    Eye,
    History,
    Loader2,
    SkipForward,
    Trophy,
  } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { fetchEntityThumbnails } from "$lib/api/entities";
  import { fetchPlaybackStatistics } from "$lib/api/playback-statistics";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import {
    entityReferenceToThumbnailCard,
    toAspectRatioNumeric,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-thumbnail";
  import {
    ENTITY_KIND,
    PLAYBACK_EVENT_KIND,
    labelForEntityKind,
    resolveEntityHref,
    type EntityKindCode,
    type PlaybackEventKindCode,
  } from "$lib/entities/entity-codes";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import type {
    PlaybackStatisticsBucket,
    PlaybackStatisticsEntity,
    PlaybackStatisticsEvent,
    PlaybackStatisticsResponse,
  } from "$lib/api/generated/model";

  const ALL_FILTER = "all" as const;

  type TimeframeKey = "30d" | "90d" | "year" | "all";
  type KindFilter = typeof ALL_FILTER | EntityKindCode;
  type EventFilter = typeof ALL_FILTER | PlaybackEventKindCode;

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

  const KIND_FILTERS: ReadonlyArray<{ value: KindFilter; label: string }> = [
    { value: ALL_FILTER, label: "All" },
    { value: ENTITY_KIND.video, label: "Videos" },
    { value: ENTITY_KIND.movie, label: "Movies" },
    { value: ENTITY_KIND.videoSeries, label: "Series" },
    { value: ENTITY_KIND.audioTrack, label: "Tracks" },
    { value: ENTITY_KIND.audioLibrary, label: "Audio" },
    { value: ENTITY_KIND.book, label: "Books" },
    { value: ENTITY_KIND.gallery, label: "Galleries" },
    { value: ENTITY_KIND.image, label: "Images" },
  ];

  const EVENT_FILTERS: ReadonlyArray<{ value: EventFilter; label: string }> = [
    { value: ALL_FILTER, label: "All" },
    { value: PLAYBACK_EVENT_KIND.completed, label: "Plays" },
    { value: PLAYBACK_EVENT_KIND.skipped, label: "Skips" },
  ];
  const STATS_THUMBNAIL_HEIGHT_REM = 3.75;
  const DAILY_ACTIVITY_VISIBLE_ROW_LIMIT = 15;
  const DAILY_ACTIVITY_ROW_HEIGHT_REM = 4.35;
  const DAILY_ACTIVITY_ROW_GAP_REM = 0.5;
  const DAILY_ACTIVITY_LIST_MAX_HEIGHT_REM =
    DAILY_ACTIVITY_VISIBLE_ROW_LIMIT * DAILY_ACTIVITY_ROW_HEIGHT_REM +
    (DAILY_ACTIVITY_VISIBLE_ROW_LIMIT - 1) * DAILY_ACTIVITY_ROW_GAP_REM;

  const nsfw = useNsfw();

  let timeframe = $state<TimeframeKey>("year");
  let kindFilter = $state<KindFilter>(ALL_FILTER);
  let eventFilter = $state<EventFilter>(PLAYBACK_EVENT_KIND.completed);
  let stats = $state<PlaybackStatisticsResponse | null>(null);
  let thumbnailCardsById = $state.raw<Map<string, EntityThumbnailCard>>(new Map());
  let loading = $state(true);
  let error = $state<string | null>(null);
  let activeRequest = 0;
  let selectedChartDate = $state<string | null>(null);

  const topEntities = $derived(stats?.topEntities ?? []);
  const recentEvents = $derived(stats?.recentEvents ?? []);
  const dailyEvents = $derived(stats?.dailyEvents ?? []);
  const dailyChartBuckets = $derived.by(() => {
    const activeBuckets = dailyEvents.filter((bucket) => countBucketEvents(bucket) > 0);
    return activeBuckets.length > 0 ? activeBuckets : dailyEvents;
  });
  const dailyActivityBuckets = $derived.by(() => [...dailyChartBuckets].reverse());
  const maxDailyEvents = $derived(
    Math.max(1, ...dailyChartBuckets.map((bucket) => countBucketEvents(bucket))),
  );
  const selectedChartBucket = $derived.by(() => {
    if (dailyActivityBuckets.length === 0) return null;
    if (selectedChartDate) {
      const selected = dailyActivityBuckets.find((bucket) => bucket.date === selectedChartDate);
      if (selected) return selected;
    }
    return dailyActivityBuckets[0] ?? null;
  });
  const dailyActivityLabel = $derived(activityLabelFor(eventFilter));
  const showCompletedLegend = $derived(eventFilter !== PLAYBACK_EVENT_KIND.skipped);
  const showSkippedLegend = $derived(eventFilter !== PLAYBACK_EVENT_KIND.completed);
  const dailyActivityCountLabel = $derived(
    `${formatNumber(dailyActivityBuckets.length)} active ${dailyActivityBuckets.length === 1 ? "day" : "days"}`,
  );
  const selectedChartTotal = $derived(selectedChartBucket ? countBucketEvents(selectedChartBucket) : 0);
  const summaryFrom = $derived(stats ? formatDate(stats.from) : "");
  const summaryTo = $derived(stats ? formatDate(stats.to) : "");
  const showEmpty = $derived(!loading && !error && (stats?.totalEvents ?? 0) === 0);

  $effect(() => {
    const params = buildQuery(timeframe, kindFilter, eventFilter, nsfw.mode === "off");
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
        error = err instanceof Error ? err.message : "Failed to load playback statistics";
      })
      .finally(() => {
        if (requestId === activeRequest) loading = false;
      });

    return () => controller.abort();
  });

  async function loadStatistics(
    params: ReturnType<typeof buildQuery>,
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
  ) {
    const to = new Date();
    const from = fromForTimeframe(selectedTimeframe, to);
    return {
      from: from.toISOString(),
      to: to.toISOString(),
      kind: selectedKind === ALL_FILTER ? undefined : selectedKind,
      eventKind: selectedEvent === ALL_FILTER ? undefined : selectedEvent,
      hideNsfw,
    };
  }

  function fromForTimeframe(selectedTimeframe: TimeframeKey, to: Date): Date {
    const option = TIMEFRAMES.find((item) => item.key === selectedTimeframe);
    if (!option || option.days == null) return new Date("1970-01-01T00:00:00.000Z");

    const from = new Date(to);
    from.setUTCDate(from.getUTCDate() - option.days);
    return from;
  }

  function isAbortError(err: unknown): boolean {
    return err instanceof DOMException && err.name === "AbortError";
  }

  function countBucketEvents(bucket: PlaybackStatisticsBucket): number {
    return Number(bucket.completedCount) + Number(bucket.skippedCount);
  }

  function entityIdsForStatistics(response: PlaybackStatisticsResponse): string[] {
    return [
      ...new Set([
        ...response.topEntities.map((entity) => entity.id),
        ...response.recentEvents.map((event) => event.entityId),
      ]),
    ];
  }

  function formatNumber(value: number | string | null | undefined): string {
    return Number(value ?? 0).toLocaleString();
  }

  function formatDate(value: string): string {
    return new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric", year: "numeric" })
      .format(new Date(value));
  }

  function formatShortDate(value: string): string {
    return formatDateOnly(value, { month: "short", day: "numeric" });
  }

  function formatDateOnly(value: string, options: Intl.DateTimeFormatOptions): string {
    const [year, month, day] = value.split("-").map(Number);
    if (!year || !month || !day) return value;
    return new Intl.DateTimeFormat(undefined, options).format(new Date(year, month - 1, day));
  }

  function formatEventTime(value: string): string {
    return new Intl.DateTimeFormat(undefined, {
      month: "short",
      day: "numeric",
      hour: "numeric",
      minute: "2-digit",
    }).format(new Date(value));
  }

  function eventLabel(kind: string): string {
    return kind === PLAYBACK_EVENT_KIND.skipped ? "Skipped" : "Played";
  }

  function eventTone(kind: string): string {
    return kind === PLAYBACK_EVENT_KIND.skipped
      ? "border-warning/30 bg-warning-muted/30 text-warning-text"
      : "border-border-accent bg-accent-950/40 text-text-accent-bright";
  }

  function activityLabelFor(selectedEvent: EventFilter): string {
    if (selectedEvent === PLAYBACK_EVENT_KIND.completed) return "Completed plays by active day";
    if (selectedEvent === PLAYBACK_EVENT_KIND.skipped) return "Skips by active day";
    return "Plays and skips by active day";
  }

  function chartBucketLabel(bucket: PlaybackStatisticsBucket): string {
    const completed = Number(bucket.completedCount);
    const skipped = Number(bucket.skippedCount);
    const total = completed + skipped;
    return `${formatDateOnly(bucket.date, { month: "long", day: "numeric", year: "numeric" })}: ${formatNumber(total)} events, ${formatNumber(completed)} plays, ${formatNumber(skipped)} skips`;
  }

  function chartBucketSummary(bucket: PlaybackStatisticsBucket): string {
    const completed = Number(bucket.completedCount);
    const skipped = Number(bucket.skippedCount);
    const parts: string[] = [];
    if (showCompletedLegend) parts.push(`${formatNumber(completed)} ${completed === 1 ? "play" : "plays"}`);
    if (showSkippedLegend) parts.push(`${formatNumber(skipped)} ${skipped === 1 ? "skip" : "skips"}`);
    return parts.join(" · ");
  }

  function chartBucketShareWidth(value: number): string {
    if (value <= 0 || maxDailyEvents <= 0) return "0%";
    return `${Math.min(100, Math.max(2, Math.round((value / maxDailyEvents) * 100)))}%`;
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

  function thumbnailWidth(card: EntityThumbnailCard): string {
    const ratio = toAspectRatioNumeric(card.aspectRatio);
    return `${(STATS_THUMBNAIL_HEIGHT_REM * ratio).toFixed(3)}rem`;
  }

  function entityHref(entity: Pick<PlaybackStatisticsEntity, "id" | "kind">): string | undefined {
    return resolveEntityHref(entity.kind, entity.id);
  }

  function eventHref(event: Pick<PlaybackStatisticsEvent, "entityId" | "entityKind">): string | undefined {
    return resolveEntityHref(event.entityKind, event.entityId);
  }

  function selectTimeframe(value: TimeframeKey) {
    timeframe = value;
    selectedChartDate = null;
  }

  function selectKind(value: KindFilter) {
    kindFilter = value;
    selectedChartDate = null;
  }

  function selectEvent(value: EventFilter) {
    eventFilter = value;
    selectedChartDate = null;
  }

  function selectChartBucket(date: string) {
    selectedChartDate = date;
  }
</script>

<svelte:head>
  <title>Playback Stats · Prismedia</title>
</svelte:head>

<div class="space-y-3 pb-6">
  <section class="surface-panel overflow-hidden">
    <div class="border-b border-border-subtle bg-gradient-to-br from-surface-2 via-surface-2 to-surface-1 px-3 py-3 sm:px-4">
      <div class="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <div class="min-w-0 space-y-2">
          <div class="flex items-center gap-2 text-[0.64rem] uppercase tracking-[0.18em] text-accent-300">
            <ChartNoAxesCombined class="h-3.5 w-3.5" />
            Playback History
          </div>
          <h1 class="font-heading text-xl text-text-primary sm:text-2xl">Playback Stats</h1>
          <p class="max-w-2xl text-xs text-text-muted">
            {#if stats}
              {summaryFrom} - {summaryTo}
            {:else}
              Loading playback history
            {/if}
          </p>
        </div>

        <div class="flex max-w-full flex-wrap gap-1.5 lg:justify-end">
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
      </div>
    </div>

    <div class="flex gap-1.5 overflow-x-auto px-3 py-2 sm:px-4">
      {#each KIND_FILTERS as option (option.value)}
        <Button
          variant={kindFilter === option.value ? "primary" : "ghost"}
          size="sm"
          class="h-6 shrink-0 px-2 text-[0.7rem]"
          onclick={() => selectKind(option.value)}
        >
          {option.label}
        </Button>
      {/each}
    </div>
  </section>

  {#if error}
    <div class="surface-panel border-l-2 border-error px-3 py-2 text-sm text-error-text" role="alert">
      {error}
    </div>
  {/if}

  <section class="surface-panel overflow-hidden">
    <div class="grid divide-y divide-border-subtle sm:grid-cols-2 sm:divide-x sm:divide-y-0 lg:grid-cols-4">
      <div class="min-h-20 px-3 py-3">
        <div class="flex items-center justify-between text-text-muted">
          <span class="text-mono-sm uppercase tracking-[0.14em]">Total</span>
          <Activity class="h-3.5 w-3.5" />
        </div>
        <div class="mt-1.5 font-heading text-2xl font-semibold text-text-primary">
          {loading ? "-" : formatNumber(stats?.totalEvents)}
        </div>
        <div class="mt-0.5 text-xs text-text-muted">Events in range</div>
      </div>

      <div class="min-h-20 px-3 py-3">
        <div class="flex items-center justify-between text-text-muted">
          <span class="text-mono-sm uppercase tracking-[0.14em]">Plays</span>
          <Eye class="h-3.5 w-3.5" />
        </div>
        <div class="mt-1.5 font-heading text-2xl font-semibold text-text-accent-bright">
          {loading ? "-" : formatNumber(stats?.completedCount)}
        </div>
        <div class="mt-0.5 text-xs text-text-muted">Completed events</div>
      </div>

      <div class="min-h-20 px-3 py-3">
        <div class="flex items-center justify-between text-text-muted">
          <span class="text-mono-sm uppercase tracking-[0.14em]">Skips</span>
          <SkipForward class="h-3.5 w-3.5" />
        </div>
        <div class="mt-1.5 font-heading text-2xl font-semibold text-warning-text">
          {loading ? "-" : formatNumber(stats?.skippedCount)}
        </div>
        <div class="mt-0.5 text-xs text-text-muted">Quick exits</div>
      </div>

      <div class="min-h-20 px-3 py-3">
        <div class="flex items-center justify-between text-text-muted">
          <span class="text-mono-sm uppercase tracking-[0.14em]">Items</span>
          <Trophy class="h-3.5 w-3.5" />
        </div>
        <div class="mt-1.5 font-heading text-2xl font-semibold text-text-primary">
          {loading ? "-" : formatNumber(stats?.distinctEntityCount)}
        </div>
        <div class="mt-0.5 text-xs text-text-muted">Distinct entities</div>
      </div>
    </div>
  </section>

  {#if loading}
    <div class="surface-panel flex min-h-40 items-center justify-center">
      <Loader2 class="h-5 w-5 animate-spin text-accent-300" />
    </div>
  {:else if showEmpty}
    <div class="surface-panel flex min-h-40 flex-col items-center justify-center px-4 text-center">
      <History class="h-6 w-6 text-text-muted" />
      <h2 class="mt-2 font-heading text-base text-text-primary">No playback history yet</h2>
      <p class="mt-1 max-w-md text-sm text-text-muted">
        Completed and skipped events will appear here as playback history is recorded.
      </p>
    </div>
  {:else}
    <section class="grid gap-3 xl:grid-cols-[minmax(0,1.25fr)_minmax(320px,0.75fr)]">
      <section class="surface-panel overflow-hidden">
        <div class="flex items-center justify-between gap-3 border-b border-border-subtle px-3 py-2.5">
          <div>
            <h2 class="font-heading text-base text-text-primary">Daily Activity</h2>
            <p class="text-xs text-text-muted">{dailyActivityLabel}</p>
          </div>
          <span class="rounded-xs border border-border-subtle bg-surface-2 px-2 py-1 font-mono text-[0.68rem] text-text-muted">
            {dailyActivityCountLabel}
          </span>
        </div>

        <div class="px-3 py-3">
          <div class="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(220px,0.34fr)]">
            <div
              class="space-y-2 overflow-y-auto pr-1"
              style:max-height={`${DAILY_ACTIVITY_LIST_MAX_HEIGHT_REM}rem`}
            >
              {#each dailyActivityBuckets as bucket (bucket.date)}
                {@const completed = Number(bucket.completedCount)}
                {@const skipped = Number(bucket.skippedCount)}
                {@const total = completed + skipped}
                <button
                  type="button"
                  class={cn(
                    "w-full rounded-xs border px-3 py-2 text-left transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-500/25 focus-visible:ring-offset-1 focus-visible:ring-offset-bg",
                    selectedChartBucket?.date === bucket.date
                      ? "border-accent-300/70 bg-accent-950/20 shadow-[0_0_18px_rgba(199, 201, 204,0.16),inset_0_1px_0_rgba(255,255,255,0.06)]"
                      : "border-border-subtle bg-surface-2/60 hover:border-border-accent hover:bg-surface-3/70",
                  )}
                  title={`${formatShortDate(bucket.date)}: ${total} events`}
                  aria-label={chartBucketLabel(bucket)}
                  aria-pressed={selectedChartBucket?.date === bucket.date}
                  onclick={() => selectChartBucket(bucket.date)}
                >
                  <span class="flex items-start justify-between gap-3">
                    <span class="min-w-0">
                      <span class="block truncate text-[0.82rem] font-medium text-text-primary">
                        {formatDateOnly(bucket.date, { month: "long", day: "numeric", year: "numeric" })}
                      </span>
                      <span class="mt-0.5 block text-xs text-text-muted">{chartBucketSummary(bucket)}</span>
                    </span>
                    <span class="shrink-0 font-heading text-lg font-semibold text-text-primary">{formatNumber(total)}</span>
                  </span>

                  <span class="mt-2 flex h-2 overflow-hidden rounded-xs border border-border-subtle bg-surface-1/80" aria-hidden="true">
                    {#if completed > 0}
                      <span
                        class="h-full bg-accent-300/90 shadow-[0_0_12px_rgba(199, 201, 204,0.24)]"
                        style:width={chartBucketShareWidth(completed)}
                      ></span>
                    {/if}
                    {#if skipped > 0}
                      <span
                        class="h-full bg-warning/80"
                        style:width={chartBucketShareWidth(skipped)}
                      ></span>
                    {/if}
                  </span>
                </button>
              {/each}
            </div>

            <div class="border-t border-border-subtle pt-3 lg:border-l lg:border-t-0 lg:pl-3 lg:pt-0">
              {#if selectedChartBucket}
                <span class="text-mono-sm uppercase tracking-[0.14em] text-text-muted">Selected Day</span>
                <div class="mt-1 font-heading text-lg font-semibold text-text-primary">
                  {formatDateOnly(selectedChartBucket.date, { month: "long", day: "numeric", year: "numeric" })}
                </div>
                <div class="mt-3 flex items-end gap-2">
                  <span class="font-heading text-4xl font-semibold text-text-primary">{formatNumber(selectedChartTotal)}</span>
                  <span class="pb-1 text-xs text-text-muted">{selectedChartTotal === 1 ? "event" : "events"}</span>
                </div>
                <div class="mt-3 flex flex-wrap gap-2 text-xs">
                  {#if showCompletedLegend}
                    <span class="rounded-xs border border-border-accent bg-accent-950/40 px-1.5 py-0.5 font-mono text-text-accent-bright">
                      {formatNumber(selectedChartBucket.completedCount)} plays
                    </span>
                  {/if}
                  {#if showSkippedLegend}
                    <span class="rounded-xs border border-warning/30 bg-warning-muted/30 px-1.5 py-0.5 font-mono text-warning-text">
                      {formatNumber(selectedChartBucket.skippedCount)} skips
                    </span>
                  {/if}
                </div>
                <div class="mt-4 flex items-center gap-4 text-xs text-text-muted">
                  {#if showCompletedLegend}
                    <span class="inline-flex items-center gap-1.5"><span class="h-2 w-2 rounded-xs bg-accent-300"></span>Plays</span>
                  {/if}
                  {#if showSkippedLegend}
                    <span class="inline-flex items-center gap-1.5"><span class="h-2 w-2 rounded-xs bg-warning"></span>Skips</span>
                  {/if}
                </div>
              {/if}
            </div>
          </div>
        </div>
      </section>

      <section class="surface-panel overflow-hidden">
        <div class="flex items-center justify-between gap-3 border-b border-border-subtle px-3 py-2.5">
          <div>
            <h2 class="font-heading text-base text-text-primary">Top Entities</h2>
            <p class="text-xs text-text-muted">Ranked by this window</p>
          </div>
          <Trophy class="h-3.5 w-3.5 text-accent-300" />
        </div>

        <div class="divide-y divide-border-subtle">
          {#each topEntities as item, index (item.id)}
            {@const href = entityHref(item)}
            {@const thumbnail = topEntityThumbnail(item)}
            <svelte:element
              this={href ? "a" : "div"}
              href={href ?? undefined}
              class={cn(
                "group grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2.5 px-3 py-2 transition-colors",
                href && "hover:bg-surface-2/60",
              )}
            >
              <div class="grid grid-cols-[auto_auto] items-center gap-2.5">
                <span class="w-4 text-right font-mono text-[0.64rem] text-text-disabled">{index + 1}</span>
                <div class="stats-thumb" style:width={thumbnailWidth(thumbnail)}>
                  <EntityThumbnail
                    card={thumbnail}
                    imageLoading="lazy"
                    interactive={false}
                    mediaOnly
                  />
                </div>
              </div>
              <div class="min-w-0">
                <div class="truncate text-[0.82rem] font-medium text-text-primary">{item.title}</div>
                <div class="text-xs text-text-muted">{labelForEntityKind(item.kind)}</div>
              </div>
              <div class="grid shrink-0 grid-cols-2 gap-2 text-right font-mono text-xs">
                <span class="text-text-accent-bright">{formatNumber(item.completedCount)}</span>
                <span class="text-warning-text">{formatNumber(item.skippedCount)}</span>
              </div>
            </svelte:element>
          {/each}
        </div>
      </section>
    </section>

    <section class="surface-panel overflow-hidden">
      <div class="flex items-center justify-between gap-3 border-b border-border-subtle px-3 py-2.5">
        <div>
          <h2 class="font-heading text-base text-text-primary">Recent Events</h2>
          <p class="text-xs text-text-muted">Latest playback history</p>
        </div>
        <Clock3 class="h-3.5 w-3.5 text-text-muted" />
      </div>

      <div class="divide-y divide-border-subtle">
        {#each recentEvents as event (event.id)}
          {@const href = eventHref(event)}
          {@const thumbnail = recentEventThumbnail(event)}
          <svelte:element
            this={href ? "a" : "div"}
            href={href ?? undefined}
            class={cn(
              "grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2.5 px-3 py-2 transition-colors",
              href && "hover:bg-surface-2/60",
            )}
          >
            <div class="stats-thumb" style:width={thumbnailWidth(thumbnail)}>
              <EntityThumbnail
                card={thumbnail}
                imageLoading="lazy"
                interactive={false}
                mediaOnly
              />
            </div>
            <div class="min-w-0">
              <div class="truncate text-[0.82rem] font-medium text-text-primary">{event.entityTitle}</div>
              <div class="text-xs text-text-muted">
                {labelForEntityKind(event.entityKind)} · {formatEventTime(event.occurredAt)}
              </div>
            </div>
            <span class={cn("shrink-0 rounded-xs border px-1.5 py-0.5 text-[0.68rem] font-medium", eventTone(event.kind))}>
              {eventLabel(event.kind)}
            </span>
          </svelte:element>
        {/each}
      </div>
    </section>
  {/if}
</div>

<style>
  .stats-thumb {
    height: 3.75rem;
    flex: 0 0 auto;
    min-width: 0;
  }

  .stats-thumb :global(.entity-thumbnail) {
    width: 100%;
    height: 100%;
  }
</style>
