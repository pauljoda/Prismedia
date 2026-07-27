<script lang="ts">
  import { CalendarClock, CalendarDays, ChevronLeft, ChevronRight, Loader2 } from "@lucide/svelte";
  import { Button, Select, cn, type SelectOption } from "@prismedia/ui-svelte";
  import { ACQUISITION_STATUS, type EntityDateTypeCode, type EntityKindCode } from "$lib/api/generated/codes";
  import type { ReleaseCalendarEvent } from "$lib/api/generated/model";
  import { fetchReleaseCalendar } from "$lib/api/release-calendar";
  import {
    monthGridRange,
    parseLocalDate,
    releaseDateLabel,
  } from "$lib/calendar/release-calendar";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { labelForEntityKind, resolveEntityHref } from "$lib/entities/entity-codes";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  const ALL = "all" as const;
  const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
  const monthFormatter = new Intl.DateTimeFormat(undefined, { month: "long", year: "numeric" });
  const agendaFormatter = new Intl.DateTimeFormat(undefined, { weekday: "long", month: "long", day: "numeric" });
  const shortDateFormatter = new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric" });

  const nsfw = useNsfw();
  const today = new Date();
  const todayKey = [
    String(today.getFullYear()).padStart(4, "0"),
    String(today.getMonth() + 1).padStart(2, "0"),
    String(today.getDate()).padStart(2, "0"),
  ].join("-");

  let month = $state(new Date(today.getFullYear(), today.getMonth(), 1, 12));
  let events = $state.raw<ReleaseCalendarEvent[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let kindFilter = $state<typeof ALL | EntityKindCode>(ALL);
  let dateFilter = $state<typeof ALL | EntityDateTypeCode>(ALL);
  let requestSequence = 0;

  const range = $derived(monthGridRange(month));
  const monthTitle = $derived(monthFormatter.format(month));
  const kindOptions = $derived.by((): SelectOption[] => [
    { value: ALL, label: "All media" },
    ...Array.from(new Set(events.map((event) => event.kind)))
      .sort((left, right) => labelForEntityKind(left).localeCompare(labelForEntityKind(right)))
      .map((kind) => ({ value: kind, label: labelForEntityKind(kind) })),
  ]);
  const dateOptions = $derived.by((): SelectOption[] => [
    { value: ALL, label: "All milestones" },
    ...Array.from(new Set(events.map((event) => event.dateType)))
      .sort((left, right) => releaseDateLabel(left).localeCompare(releaseDateLabel(right)))
      .map((type) => ({ value: type, label: releaseDateLabel(type) })),
  ]);
  const filteredEvents = $derived(
    events.filter((event) =>
      (kindFilter === ALL || event.kind === kindFilter)
      && (dateFilter === ALL || event.dateType === dateFilter)),
  );
  const eventsByDate = $derived.by(() => {
    const grouped: Record<string, ReleaseCalendarEvent[]> = Object.create(null);
    for (const event of filteredEvents) {
      const dayEvents = grouped[event.date] ?? [];
      dayEvents.push(event);
      grouped[event.date] = dayEvents;
    }
    for (const dayEvents of Object.values(grouped)) {
      dayEvents.sort((left, right) => left.title.localeCompare(right.title)
        || releaseDateLabel(left.dateType).localeCompare(releaseDateLabel(right.dateType)));
    }
    return grouped;
  });
  const agendaDays = $derived(range.days.filter((day) => (eventsByDate[day]?.length ?? 0) > 0));

  $effect(() => {
    const start = range.start;
    const end = range.end;
    const visibility = nsfw.mode;
    const sequence = ++requestSequence;
    const controller = new AbortController();
    loading = true;
    error = null;

    void fetchReleaseCalendar(start, end, { signal: controller.signal })
      .then((response) => {
        if (sequence !== requestSequence) return;
        events = response;
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted || sequence !== requestSequence) return;
        error = reason instanceof Error ? reason.message : "Failed to load the release calendar";
      })
      .finally(() => {
        if (sequence === requestSequence) loading = false;
      });

    void visibility;
    return () => controller.abort();
  });

  function moveMonth(offset: number): void {
    month = new Date(month.getFullYear(), month.getMonth() + offset, 1, 12);
  }

  function showToday(): void {
    const now = new Date();
    month = new Date(now.getFullYear(), now.getMonth(), 1, 12);
  }

  function isInMonth(day: string): boolean {
    const date = parseLocalDate(day);
    return date.getFullYear() === month.getFullYear() && date.getMonth() === month.getMonth();
  }

  function eventHref(event: ReleaseCalendarEvent): string | undefined {
    return resolveEntityHref(event.kind, event.entityId);
  }

  function gateLabel(event: ReleaseCalendarEvent): string | null {
    if (!event.isSearchGate) return null;
    if (event.isSearchEligible) return "Search ready";
    if (event.searchNotBefore) return `Searches ${shortDateFormatter.format(parseLocalDate(event.searchNotBefore))}`;
    return "Search gate";
  }
</script>

<svelte:head>
  <title>Release Calendar · Prismedia</title>
</svelte:head>

<section class="space-y-3 pb-6">
  <header class="flex flex-col gap-3 border-l-2 border-[var(--color-material-spectrum-violet)] pl-3 lg:flex-row lg:items-end lg:justify-between">
    <div class="min-w-0">
      <h1 class="flex items-center gap-2 font-heading text-xl font-semibold tracking-tight text-text-primary">
        <CalendarDays class="h-5 w-5 text-text-muted" aria-hidden="true" />
        Release calendar
      </h1>
      <p class="mt-1 max-w-2xl text-sm leading-relaxed text-text-muted">
        Theatrical, streaming, digital, physical, air, and publication dates across monitored requests.
      </p>
    </div>

    <div class="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
      <Select
        size="sm"
        value={kindFilter}
        options={kindOptions}
        ariaLabel="Filter release calendar by media kind"
        onchange={(value) => (kindFilter = value as typeof kindFilter)}
      />
      <Select
        size="sm"
        value={dateFilter}
        options={dateOptions}
        ariaLabel="Filter release calendar by milestone"
        onchange={(value) => (dateFilter = value as typeof dateFilter)}
      />
    </div>
  </header>

  {#if error}
    <div class="surface-panel border-l-2 border-error px-3 py-2 text-sm text-error-text" role="alert">
      {error}
    </div>
  {/if}

  <section class="surface-panel overflow-hidden">
    <div class="flex flex-wrap items-center justify-between gap-2 border-b border-border-subtle px-3 py-2.5">
      <div class="flex items-center gap-1">
        <Button variant="ghost" size="sm" aria-label="Previous month" onclick={() => moveMonth(-1)}>
          <ChevronLeft class="h-4 w-4" />
        </Button>
        <Button variant="ghost" size="sm" onclick={showToday}>Today</Button>
        <Button variant="ghost" size="sm" aria-label="Next month" onclick={() => moveMonth(1)}>
          <ChevronRight class="h-4 w-4" />
        </Button>
      </div>
      <div class="flex items-center gap-2">
        {#if loading}<Loader2 class="h-3.5 w-3.5 animate-spin text-text-muted" aria-label="Loading calendar" />{/if}
        <h2 class="font-heading text-base font-semibold text-text-primary">{monthTitle}</h2>
      </div>
    </div>

    <div class="hidden grid-cols-7 border-b border-border-subtle bg-surface-1 md:grid">
      {#each WEEKDAYS as weekday (weekday)}
        <div class="px-2 py-1.5 text-center text-[0.66rem] font-semibold uppercase tracking-[0.16em] text-text-muted">
          {weekday}
        </div>
      {/each}
    </div>

    <div class="hidden grid-cols-7 md:grid">
      {#each range.days as day (day)}
        {@const dayEvents = eventsByDate[day] ?? []}
        {@const dayDate = parseLocalDate(day)}
        <div class={cn(
          "min-h-32 border-b border-r border-border-subtle p-1.5 last:border-r-0",
          !isInMonth(day) && "bg-surface-1/45",
          day === todayKey && "bg-surface-2/55 shadow-[inset_0_2px_0_var(--color-material-spectrum-violet)]",
        )}>
          <div class={cn(
            "mb-1 font-mono text-[0.68rem] text-text-secondary",
            !isInMonth(day) && "text-text-disabled",
          )}>
            {dayDate.getDate()}
          </div>
          <div class="space-y-1">
            {#each dayEvents.slice(0, 4) as event (`${event.monitorId}:${event.dateType}:${event.date}`)}
              {@render eventRow(event, true)}
            {/each}
            {#if dayEvents.length > 4}
              <div class="px-1 text-[0.65rem] text-text-muted">+{dayEvents.length - 4} more</div>
            {/if}
          </div>
        </div>
      {/each}
    </div>

    <div class="md:hidden">
      {#if !loading && agendaDays.length === 0}
        <div class="flex min-h-56 flex-col items-center justify-center px-6 text-center">
          <CalendarClock class="h-6 w-6 text-text-muted" aria-hidden="true" />
          <h3 class="mt-2 font-heading text-base text-text-primary">No release dates in this view</h3>
          <p class="mt-1 max-w-sm text-sm text-text-muted">Try another month or clear the media and milestone filters.</p>
        </div>
      {:else}
        {#each agendaDays as day (day)}
          <section class="border-b border-border-subtle last:border-b-0">
            <div class={cn(
              "sticky top-0 z-10 border-b border-border-subtle bg-surface-panel/95 px-3 py-2 backdrop-blur-sm",
              day === todayKey && "shadow-[inset_2px_0_0_var(--color-material-spectrum-violet)]",
            )}>
              <h3 class="text-xs font-semibold text-text-primary">{agendaFormatter.format(parseLocalDate(day))}</h3>
            </div>
            <div class="space-y-1.5 p-2">
              {#each eventsByDate[day] ?? [] as event (`${event.monitorId}:${event.dateType}:${event.date}`)}
                {@render eventRow(event, false)}
              {/each}
            </div>
          </section>
        {/each}
      {/if}
    </div>
  </section>
</section>

{#snippet eventRow(event: ReleaseCalendarEvent, compact: boolean)}
  {@const href = eventHref(event)}
  {@const accent = entityAccentForKind(event.kind).primary}
  {@const gate = gateLabel(event)}
  {#if href}
    <a
      href={href}
      class={cn(
        "block min-w-0 border-l-2 border-border-subtle bg-surface-1 transition-colors hover:bg-surface-2 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-border-accent",
        compact ? "px-1.5 py-1" : "px-3 py-2.5",
      )}
      style:border-left-color={accent}
    >
      {@render eventContent(event, compact, gate)}
    </a>
  {:else}
    <div
      class={cn("min-w-0 border-l-2 bg-surface-1", compact ? "px-1.5 py-1" : "px-3 py-2.5")}
      style:border-left-color={accent}
    >
      {@render eventContent(event, compact, gate)}
    </div>
  {/if}
{/snippet}

{#snippet eventContent(event: ReleaseCalendarEvent, compact: boolean, gate: string | null)}
  <div class="min-w-0">
    <div class={cn("truncate font-medium text-text-primary", compact ? "text-[0.68rem]" : "text-sm")}>{event.title}</div>
    <div class={cn("mt-0.5 flex min-w-0 flex-wrap items-center gap-x-1.5 gap-y-0.5 text-text-muted", compact ? "text-[0.6rem]" : "text-[0.72rem]")}>
      <span>{releaseDateLabel(event.dateType)}</span>
      {#if !compact}<span>·</span><span>{labelForEntityKind(event.kind)}</span>{/if}
      {#if gate}
        <span>·</span>
        <span class={event.isSearchEligible ? "text-success-text" : "text-text-accent"}>{gate}</span>
      {/if}
      {#if event.acquisitionStatus === ACQUISITION_STATUS.waitingForRelease && !event.isSearchGate}
        <span>·</span><span>Monitored</span>
      {/if}
    </div>
  </div>
{/snippet}
