<script lang="ts">
  import { resolve } from "$app/paths";
  import { ChevronRight, HardDriveDownload } from "@lucide/svelte";
  import { Meter, Skeleton, StatusLed, cn } from "@prismedia/ui-svelte";
  import { acquisitionStatusLabel } from "$lib/requests/acquisition-status";
  import ActivityWeek from "./ActivityWeek.svelte";
  import CompositionBeam from "./CompositionBeam.svelte";
  import { digestDownloads, needsYouItems, type LibraryPulse, type WeekActivity } from "./dashboard-data";
  import { libraryTotal, type LibraryFamily } from "../library-composition";

  interface Props {
    families: LibraryFamily[];
    /** Null while loading; undefined when activity could not be read. */
    week: WeekActivity | null | undefined;
    pulse: LibraryPulse | null;
    class?: string;
  }

  let { families, week, pulse, class: className }: Props = $props();

  const numberFormat = new Intl.NumberFormat();
  const present = $derived(families.filter((family) => family.count > 0));
  const total = $derived(libraryTotal(families));
  const digest = $derived(digestDownloads(pulse?.downloads ?? null));
  const leadTransfer = $derived(digest.transferring[0] ?? null);
  const decisions = $derived(pulse ? needsYouItems(pulse) : []);

  /** Transfer progress arrives as a 0..1 fraction serialized as number or string. */
  function progressPercent(value: number | string | null): number {
    const parsed = Number(value ?? 0);
    return Number.isFinite(parsed) ? Math.round(Math.min(1, Math.max(0, parsed)) * 100) : 0;
  }
</script>

<!--
  Top to bottom: what you own, what you did, what is happening now. The top edge is the page's one
  light moment: white light dispersing into the library's composition.
-->
<aside
  aria-label="Library pulse"
  class={cn(
    "relative flex flex-col overflow-hidden rounded-[var(--radius-xl)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] shadow-[var(--shadow-card)]",
    className,
  )}
>
  <CompositionBeam {families} thickness={2} source={0.34} class="absolute inset-x-0 top-0" />

  <section class="flex flex-col gap-3 p-4 pt-5">
    <div class="flex items-baseline justify-between gap-3">
      <h2 class="font-mono text-[0.68rem] uppercase tracking-[0.16em] text-text-muted">Library</h2>
      {#if present.length > 0}
        <span class="text-caption text-text-muted">{present.length} families</span>
      {/if}
    </div>
    {#if families.length === 0}
      <Skeleton class="h-9 w-28" />
      <Skeleton class="h-16 w-full" />
    {:else}
      <p class="flex items-baseline gap-2">
        <span class="font-heading text-3xl font-semibold tabular-nums tracking-tight text-text-primary">{numberFormat.format(total)}</span>
        <span class="text-sm text-text-muted">items</span>
      </p>
      <ul class="grid grid-cols-2 gap-x-4 gap-y-1">
        {#each present as family (family.kind)}
          <li>
            <a
              href={family.href}
              class="group flex min-h-7 items-center gap-2 text-caption text-text-secondary transition-colors hover:text-text-primary"
            >
              <span
                class="h-2.5 w-[3px] shrink-0 rounded-[1px]"
                style:background="linear-gradient(180deg, {family.accent.primary}, {family.accent.secondary})"
                aria-hidden="true"
              ></span>
              <span class="min-w-0 flex-1 truncate">{family.label}</span>
              <span class="font-mono tabular-nums text-text-muted group-hover:text-text-secondary">{numberFormat.format(family.count)}</span>
            </a>
          </li>
        {/each}
      </ul>
    {/if}
  </section>

  {#if week !== undefined}
    <section class="border-t border-[var(--color-border-subtle)] p-4">
      {#if week}
        <ActivityWeek {week} />
      {:else}
        <Skeleton class="h-28 w-full" />
      {/if}
    </section>
  {/if}

  {#if pulse}
    <section class="flex flex-col gap-2.5 border-t border-[var(--color-border-subtle)] p-4" aria-label="Live work">
      <div class="flex items-baseline justify-between gap-3">
        <h2 class="font-mono text-[0.68rem] uppercase tracking-[0.16em] text-text-muted">Live</h2>
        {#if pulse.worker}
          <a href={resolve("/jobs")} class="inline-flex items-center gap-1.5 text-caption text-text-muted hover:text-text-primary" title={pulse.worker.tooltip}>
            <StatusLed status={pulse.worker.led} size="sm" pulse={pulse.worker.pulse} />
            {pulse.worker.label}
          </a>
        {/if}
      </div>

      {#if pulse.downloads !== null}
        <a
          href={resolve("/downloads")}
          class="group flex flex-col gap-2 rounded-[var(--radius-sm)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] p-3 transition-colors hover:border-[var(--color-border-default)]"
        >
          <span class="flex items-center gap-2 text-sm text-text-secondary group-hover:text-text-primary">
            <HardDriveDownload class="h-3.5 w-3.5 text-text-muted" aria-hidden="true" />
            Downloads
            <span class="ml-auto font-mono text-caption tabular-nums text-text-muted">
              {digest.transferring.length} moving · {digest.waiting.length} waiting
            </span>
          </span>
          {#if leadTransfer}
            <span class="flex min-w-0 flex-col gap-1">
              <span class="flex items-baseline justify-between gap-2 text-caption">
                <span class="min-w-0 truncate text-text-secondary">{leadTransfer.title}</span>
                <span class="shrink-0 font-mono text-text-muted">{acquisitionStatusLabel(leadTransfer.status)}</span>
              </span>
              <Meter value={progressPercent(leadTransfer.progress)} />
            </span>
          {/if}
        </a>
      {/if}

      {#if decisions.length > 0}
        <ul class="flex flex-col">
          {#each decisions as item (item.key)}
            {@const Icon = item.icon}
            <li class="border-b border-[var(--color-border-subtle)] last:border-b-0">
              <a
                href={item.href}
                class="group flex min-h-9 min-w-0 items-center gap-2 text-caption text-text-secondary transition-colors hover:text-text-primary"
              >
                <Icon class="h-3.5 w-3.5 shrink-0 text-text-muted" aria-hidden="true" />
                <span class="shrink-0 font-medium text-text-primary">{item.action}</span>
                <span class="min-w-0 flex-1 truncate text-text-muted">
                  {item.count > 1 ? `${item.count} waiting` : item.subject}
                </span>
                <ChevronRight class="h-3.5 w-3.5 shrink-0 text-text-disabled group-hover:text-text-muted" aria-hidden="true" />
              </a>
            </li>
          {/each}
        </ul>
      {/if}
    </section>
  {/if}
</aside>
