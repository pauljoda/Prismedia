<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import { PRISM_HEAT_STOPS, prismHeatColor } from "$lib/stats/prism-scale";
  import {
    formatHourLabel,
    weekdayLabels,
    type ConsumptionRhythm,
    type ConsumptionRhythmCell,
  } from "$lib/stats/consumption-stats";

  interface Props {
    rhythm: ConsumptionRhythm;
    class?: string;
  }

  let { rhythm, class: className }: Props = $props();

  /** Hours labelled along the top; a label every hour would not fit at mobile widths. */
  const LABELLED_HOURS = [0, 6, 12, 18];

  const weekdays = weekdayLabels();
  let hoveredCell = $state<ConsumptionRhythmCell | null>(null);

  /** The heat scale as a CSS gradient stop list, so the key cannot drift from the cells. */
  const heatRamp = PRISM_HEAT_STOPS.map(
    (stop) => `${stop.color} ${(stop.offset * 100).toFixed(0)}%`,
  ).join(", ");

  const peakHourTotal = $derived(Math.max(1, ...rhythm.byHour));
  const readoutCell = $derived(hoveredCell ?? rhythm.peak);

  /**
   * A superlinear curve is what makes a long window readable: over a year almost every hour
   * collects some consumption events, so a linear ramp flattens the grid into one flat field.
   */
  function shaped(intensity: number): number {
    return intensity <= 0 ? 0 : intensity ** 1.45;
  }

  /**
   * Cells carry both hue and opacity from the shared prism heat scale, so a quiet hour stays a
   * dark cool square and the busiest hour of the week burns warm.
   */
  function cellColor(intensity: number): string {
    const t = shaped(intensity);
    if (t <= 0) return "rgb(255 255 255 / 0.035)";
    return prismHeatColor(t, 0.16 + 0.84 * t);
  }

  function cellTitle(cell: ConsumptionRhythmCell): string {
    return `${weekdays[cell.dayOfWeek]} ${formatHourLabel(cell.hour)}: ${cell.totalEvents.toLocaleString()} events`;
  }
</script>

<div class={cn("flex flex-col gap-2.5", className)} style:--heat-ramp={heatRamp}>
  <!--
    The grid restates the summary line below it, so it stays decorative for assistive technology
    while the summary carries the same information as text.
  -->
  <div class="rhythm-grid" aria-hidden="true" onpointerleave={() => (hoveredCell = null)}>
    <div class="rhythm-corner"></div>
    <div class="rhythm-hours">
      {#each Array.from({ length: 24 }, (_, hour) => hour) as hour (hour)}
        <span class="rhythm-hour-label">{LABELLED_HOURS.includes(hour) ? formatHourLabel(hour) : ""}</span>
      {/each}
    </div>

    {#each rhythm.cells as row, dayOfWeek (dayOfWeek)}
      <span class="rhythm-day-label">{weekdays[dayOfWeek]}</span>
      <div class="rhythm-row">
        {#each row as cell (cell.hour)}
          <span
            role="presentation"
            class={cn("rhythm-cell", hoveredCell === cell && "rhythm-cell-hovered")}
            style:--cell-color={cellColor(cell.intensity)}
            title={cellTitle(cell)}
            onpointerenter={() => (hoveredCell = cell)}
          ></span>
        {/each}
      </div>
    {/each}

    <div class="rhythm-corner"></div>
    <div class="rhythm-hour-histogram">
      {#each rhythm.byHour as total, hour (hour)}
        {@const share = total / peakHourTotal}
        <span class="rhythm-hour-bar-track">
          <span
            class="rhythm-hour-bar"
            style:height={`${share * 100}%`}
            style:background={prismHeatColor(share, 0.45 + 0.55 * share)}
          ></span>
        </span>
      {/each}
    </div>
  </div>

  <div class="rhythm-scale">
    <span class="rhythm-scale-label">Quiet</span>
    <span class="rhythm-scale-ramp" aria-hidden="true"></span>
    <span class="rhythm-scale-label">Busy</span>
  </div>

  <p class="rhythm-readout" aria-live="polite">
    {#if readoutCell && readoutCell.totalEvents > 0}
      <span class="rhythm-readout-strong">
        {weekdays[readoutCell.dayOfWeek]} at {formatHourLabel(readoutCell.hour)}
      </span>
      <span>
        {readoutCell.totalEvents.toLocaleString()} {readoutCell.totalEvents === 1 ? "event" : "events"}
        {#if readoutCell === rhythm.peak && !hoveredCell}· busiest hour of the week{/if}
      </span>
    {:else}
      <span>No consumption recorded in this window.</span>
    {/if}
  </p>
</div>

<style>
  .rhythm-grid {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr);
    align-items: center;
    gap: 2px 0.4rem;
    min-width: 0;
  }

  .rhythm-corner {
    width: 100%;
  }

  .rhythm-hours,
  .rhythm-row,
  .rhythm-hour-histogram {
    display: grid;
    grid-template-columns: repeat(24, minmax(0, 1fr));
    gap: 2px;
    min-width: 0;
  }

  .rhythm-hour-label,
  .rhythm-day-label {
    font-family: var(--font-mono);
    font-size: 0.58rem;
    line-height: 1;
    color: var(--color-text-disabled);
  }

  .rhythm-hour-label {
    text-align: left;
    padding-bottom: 0.15rem;
  }

  .rhythm-day-label {
    text-align: right;
    white-space: nowrap;
  }

  .rhythm-cell {
    aspect-ratio: 1;
    min-height: 0.5rem;
    border-radius: 2px;
    background: var(--cell-color);
    transition: box-shadow var(--duration-fast, 120ms) var(--ease-default, ease);
  }

  .rhythm-cell-hovered {
    box-shadow: 0 0 0 1px var(--color-border-accent-strong);
  }

  .rhythm-hour-histogram {
    align-items: end;
    height: 1.75rem;
    padding-top: 0.3rem;
  }

  .rhythm-hour-bar-track {
    display: flex;
    align-items: flex-end;
    height: 100%;
    min-width: 0;
  }

  .rhythm-hour-bar {
    width: 100%;
    min-height: 1px;
    border-radius: 1px;
  }

  .rhythm-scale {
    display: flex;
    align-items: center;
    gap: 0.45rem;
  }

  .rhythm-scale-label {
    font-family: var(--font-mono);
    font-size: 0.58rem;
    color: var(--color-text-disabled);
  }

  /* The ramp is the heatmap's key: without it the hues are decoration rather than a reading. */
  .rhythm-scale-ramp {
    flex: 0 1 7rem;
    height: 0.3rem;
    border-radius: 2px;
    background: linear-gradient(90deg, var(--heat-ramp));
    opacity: 0.85;
  }

  .rhythm-readout {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 0.45rem;
    margin: 0;
    font-family: var(--font-mono);
    font-size: 0.68rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-text-muted);
  }

  .rhythm-readout-strong {
    color: var(--color-text-primary);
  }

  @media (prefers-reduced-motion: reduce) {
    .rhythm-cell {
      transition: none;
    }
  }
</style>
