<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import { formatActiveDuration } from "$lib/stats/consumption-stats";
  import { formatRelativeTime } from "$lib/utils/format";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { WeekActivity } from "./dashboard-data";

  interface Props {
    week: WeekActivity;
  }

  let { week }: Props = $props();

  const peak = $derived(Math.max(...week.days.map((day) => day.activeSeconds), 0));
  const quiet = $derived(week.days.every((day) => day.activeSeconds === 0 && day.opened === 0));
  const weekSeconds = $derived(week.days.reduce((sum, day) => sum + day.activeSeconds, 0));
  const weekOpened = $derived(week.days.reduce((sum, day) => sum + day.opened, 0));
  const summary = $derived(
    week.days.map((day) => `${day.label} ${formatActiveDuration(day.activeSeconds)}`).join(", "),
  );
  const latestHref = $derived(week.latest ? resolveEntityHref(week.latest.kind, week.latest.entityId) : undefined);

  /** Bar height as a share of the week's busiest day; a day with any activity keeps a visible stub. */
  function barHeight(seconds: number): string {
    if (peak === 0 || seconds === 0) return "2px";
    return `max(4px, ${(seconds / peak) * 100}%)`;
  }
</script>

<div class="flex flex-col gap-3">
  <div class="flex items-baseline justify-between gap-3">
    <h3 class="font-mono text-[0.68rem] uppercase tracking-[0.16em] text-text-muted">This week</h3>
    {#if !quiet}
      <span class="font-mono text-caption tabular-nums text-text-secondary">
        {formatActiveDuration(weekSeconds)} · {weekOpened} opened
      </span>
    {/if}
  </div>

  <!-- One neutral series: bar height is active time, and today is marked by brightness and its label. -->
  <div class="flex h-16 items-end gap-1.5" role="img" aria-label="Active time over the last seven days: {summary}">
    {#each week.days as day (day.key)}
      <div class="flex h-full flex-1 flex-col items-center justify-end gap-1.5">
        <div class="flex w-full flex-1 items-end">
          <span
            class={cn("block w-full rounded-t-[2px]", day.isToday ? "bg-[var(--color-text-primary)]" : "bg-[var(--color-surface-4)]")}
            style:height={barHeight(day.activeSeconds)}
          ></span>
        </div>
        <span class={cn("font-mono text-[0.62rem] leading-none", day.isToday ? "text-text-primary" : "text-text-disabled")}>
          {day.label}
        </span>
      </div>
    {/each}
  </div>

  <p class="flex flex-wrap items-baseline gap-x-2 text-caption text-text-muted">
    <span>Today</span>
    <span class="font-mono tabular-nums text-text-secondary">{formatActiveDuration(week.today.activeSeconds)}</span>
    <span aria-hidden="true">·</span>
    <span><span class="font-mono tabular-nums text-text-secondary">{week.today.opened}</span> opened</span>
    <span aria-hidden="true">·</span>
    <span><span class="font-mono tabular-nums text-text-secondary">{week.today.finished}</span> finished</span>
  </p>
  {#if week.latest}
    <a
      href={latestHref}
      class="group -mt-1 flex min-w-0 items-center gap-2 text-caption text-text-muted transition-colors hover:text-text-primary"
    >
      <span class="shrink-0">Last opened</span>
      <span class="min-w-0 truncate text-text-secondary group-hover:text-text-primary">{week.latest.title}</span>
      <span class="shrink-0 font-mono tabular-nums">{formatRelativeTime(week.latest.occurredAt, true)}</span>
    </a>
  {/if}
</div>
