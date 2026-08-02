<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import { PRISM_HEAT_STOPS } from "$lib/stats/prism-scale";
  import {
    aggregateDaySeries,
    formatDayKey,
    formatSpanLabel,
    formatActiveDuration,
    niceAxisMax,
    rollingAverage,
    type PlaybackDaySample,
    type PlaybackSpanSample,
  } from "$lib/stats/playback-stats";

  interface Props {
    series: PlaybackDaySample[];
    /** Controls which event layers remain visible when the page is filtered. */
    showAccessed?: boolean;
    showCompleted?: boolean;
    showSkipped?: boolean;
    /** Start date of the selected column. */
    selectedDate?: string | null;
    onSelect?: (date: string | null) => void;
    class?: string;
  }

  let {
    series,
    showAccessed = true,
    showCompleted = true,
    showSkipped = true,
    selectedDate = null,
    onSelect,
    class: className,
  }: Props = $props();

  const HEIGHT = 220;
  const PAD_LEFT = 34;
  const PAD_RIGHT = 8;
  const PAD_TOP = 14;
  const PAD_BOTTOM = 22;
  /** Narrowest a column may get before days are grouped instead of drawn individually. */
  const MIN_COLUMN_STEP = 3;
  /**
   * Grouping snaps to calendar-shaped runs so the axis can be described in units a reader already
   * thinks in. An arbitrary "5-day block" is harder to reason about than a week.
   */
  const GRANULARITY_STEPS = [1, 2, 3, 7, 14, 30];
  /** Minimum pixels between x-axis labels before one is dropped. */
  const LABEL_SPACING = 46;
  /** Above this many columns the axis labels months instead of individual dates. */
  const DENSE_COLUMN_THRESHOLD = 70;

  let frameWidth = $state(0);
  let hoveredIndex = $state<number | null>(null);

  const width = $derived(Math.max(frameWidth, 260));
  const plotWidth = $derived(Math.max(1, width - PAD_LEFT - PAD_RIGHT));
  const plotHeight = HEIGHT - PAD_TOP - PAD_BOTTOM;

  // Granularity follows the available width: a year of days is drawn per day on a wide panel and
  // grouped into weeks on a phone, where per-day columns would alias into noise.
  const groupSize = $derived.by(() => {
    const maxColumns = Math.max(1, Math.floor(plotWidth / MIN_COLUMN_STEP));
    const required = Math.max(1, Math.ceil(series.length / maxColumns));
    return GRANULARITY_STEPS.find((step) => step >= required) ?? required;
  });
  const columns = $derived<PlaybackSpanSample[]>(aggregateDaySeries(series, groupSize));

  const step = $derived(columns.length > 0 ? plotWidth / columns.length : plotWidth);
  const barWidth = $derived(Math.max(1.5, Math.min(20, step - (step > 5 ? 1.6 : 0.4))));
  const isDense = $derived(columns.length > DENSE_COLUMN_THRESHOLD);
  const accessesAreVisible = $derived(showAccessed);

  const peakEvents = $derived(Math.max(0, ...columns.map((column) => column.totalEvents)));
  // The axis rounds up to a readable gridline, which is not the same as the observed peak.
  const axisMax = $derived(niceAxisMax(Math.max(1, peakEvents)));

  const trendWindow = $derived(groupSize > 1 ? 4 : 7);
  const trend = $derived(
    columns.length > trendWindow
      ? rollingAverage(columns.map((column) => column.totalEvents), trendWindow)
      : [],
  );

  const columnCenter = $derived.by(() =>
    (index: number) => PAD_LEFT + step * (index + 0.5),
  );
  const valueY = $derived.by(() =>
    (value: number) => PAD_TOP + plotHeight * (1 - value / axisMax),
  );

  const selectedIndex = $derived(
    selectedDate ? columns.findIndex((column) => column.startDate === selectedDate) : -1,
  );
  const activeIndex = $derived(hoveredIndex ?? (selectedIndex >= 0 ? selectedIndex : null));
  const activeColumn = $derived(activeIndex != null ? (columns[activeIndex] ?? null) : null);

  /** The heat scale as a CSS gradient stop list, so the legend swatch matches the columns. */
  const heatRamp = PRISM_HEAT_STOPS.map(
    (stop) => `${stop.color} ${(stop.offset * 100).toFixed(0)}%`,
  ).join(", ");

  const granularityLabel = $derived.by(() => {
    if (groupSize === 1) return "day";
    if (groupSize === 7) return "week";
    if (groupSize === 14) return "fortnight";
    if (groupSize === 30) return "month";
    return `${groupSize} days`;
  });

  const trendLabel = $derived(
    groupSize === 1 ? `${trendWindow}-day average` : `${trendWindow}-point average`,
  );

  const trendPath = $derived.by(() => {
    if (trend.length === 0) return "";
    return trend
      .map((value, index) => `${index === 0 ? "M" : "L"} ${columnCenter(index).toFixed(2)} ${valueY(value).toFixed(2)}`)
      .join(" ");
  });

  interface AxisTick {
    index: number;
    label: string;
  }

  const axisTicks = $derived.by<AxisTick[]>(() => {
    if (columns.length === 0) return [];

    const ticks: AxisTick[] = [];
    let lastX = Number.NEGATIVE_INFINITY;
    if (isDense) {
      // Month starts anchor a long window; individual date labels would be unreadable.
      let previousMonth = "";
      columns.forEach((column, index) => {
        const month = column.startDate.slice(0, 7);
        if (month === previousMonth) return;
        previousMonth = month;
        const x = columnCenter(index);
        if (x - lastX < LABEL_SPACING) return;
        lastX = x;
        ticks.push({ index, label: formatDayKey(column.startDate, { month: "short" }) });
      });
      return ticks;
    }

    columns.forEach((column, index) => {
      const x = columnCenter(index);
      if (x - lastX < LABEL_SPACING) return;
      lastX = x;
      ticks.push({ index, label: formatDayKey(column.startDate, { month: "short", day: "numeric" }) });
    });
    return ticks;
  });

  function indexAtPointer(event: PointerEvent | MouseEvent): number | null {
    const bounds = (event.currentTarget as SVGElement).getBoundingClientRect();
    if (bounds.width === 0 || columns.length === 0) return null;
    const x = ((event.clientX - bounds.left) / bounds.width) * width;
    const index = Math.floor((x - PAD_LEFT) / step);
    return index >= 0 && index < columns.length ? index : null;
  }

  function handlePointerMove(event: PointerEvent) {
    hoveredIndex = indexAtPointer(event);
  }

  function handleClick(event: MouseEvent) {
    const index = indexAtPointer(event);
    if (index == null) return;
    const date = columns[index].startDate;
    onSelect?.(selectedDate === date ? null : date);
  }

  function handleKeydown(event: KeyboardEvent) {
    if (columns.length === 0) return;

    const current = selectedIndex >= 0 ? selectedIndex : columns.length - 1;
    let next: number | null = null;
    if (event.key === "ArrowLeft") next = Math.max(0, current - 1);
    else if (event.key === "ArrowRight") next = Math.min(columns.length - 1, current + 1);
    else if (event.key === "Home") next = 0;
    else if (event.key === "End") next = columns.length - 1;
    else if (event.key === "Escape" && selectedDate) next = -1;
    if (next == null) return;

    event.preventDefault();
    onSelect?.(next === -1 ? null : columns[next].startDate);
  }

  function barHeight(value: number): number {
    if (value <= 0) return 0;
    // Keep a one-event column from vanishing on a window whose peak is far higher.
    return Math.max(1.5, (value / axisMax) * plotHeight);
  }
</script>

<div class={cn("flex flex-col gap-2", className)} style:--heat-ramp={heatRamp}>
  <p class="timeline-readout" aria-live="polite">
    {#if activeColumn}
      <span class="timeline-readout-day">{formatSpanLabel(activeColumn)}</span>
      <span class="timeline-readout-detail">
        {activeColumn.totalEvents.toLocaleString()} {activeColumn.totalEvents === 1 ? "event" : "events"}
        · {activeColumn.accessedCount.toLocaleString()} opened
        · {activeColumn.completedCount.toLocaleString()} completed
        {#if activeColumn.skippedCount > 0}· {activeColumn.skippedCount.toLocaleString()} skipped{/if}
        · {formatActiveDuration(activeColumn.activeSeconds)} active
      </span>
    {:else}
      <span class="timeline-readout-detail">
        Peak {peakEvents.toLocaleString()} {peakEvents === 1 ? "event" : "events"} per {granularityLabel} ·
        hover or use arrow keys to inspect
      </span>
    {/if}
  </p>

  <!--
    Scrubbing an ordered run of columns is a one-dimensional range selection, so the chart exposes
    itself as a slider: arrow keys move the inspected column and the value text carries its readout.
  -->
  <div
    class="timeline-frame"
    bind:clientWidth={frameWidth}
    role="slider"
    tabindex="0"
    aria-label="Consumption activity over time"
    aria-orientation="horizontal"
    aria-valuemin={0}
    aria-valuemax={Math.max(0, columns.length - 1)}
    aria-valuenow={activeIndex ?? Math.max(0, columns.length - 1)}
    aria-valuetext={activeColumn
      ? `${formatSpanLabel(activeColumn)}: ${activeColumn.totalEvents.toLocaleString()} events`
      : "Nothing selected"}
    onkeydown={handleKeydown}
  >
    {#if frameWidth > 0}
      <svg
        class="timeline-svg"
        width={width}
        height={HEIGHT}
        viewBox={`0 0 ${width} ${HEIGHT}`}
        role="presentation"
        onpointermove={handlePointerMove}
        onpointerleave={() => (hoveredIndex = null)}
        onclick={handleClick}
      >
        <defs>
          <!--
            One gradient in user space fills every access column, so a column's colour is decided
            by how tall it is. That is the same encoding the rhythm heatmap uses.
          -->
          <linearGradient
            id="timeline-heat"
            x1="0"
            y1={PAD_TOP + plotHeight}
            x2="0"
            y2={PAD_TOP}
            gradientUnits="userSpaceOnUse"
          >
            {#each PRISM_HEAT_STOPS as stop (stop.offset)}
              <stop offset={stop.offset} stop-color={stop.color} />
            {/each}
          </linearGradient>
        </defs>

        {#each [0, 0.5, 1] as fraction (fraction)}
          {@const y = PAD_TOP + plotHeight * fraction}
          <line class="timeline-grid" x1={PAD_LEFT} y1={y} x2={width - PAD_RIGHT} y2={y} />
          <text class="timeline-axis-label" x={PAD_LEFT - 7} y={y + 3} text-anchor="end">
            {Math.round(axisMax * (1 - fraction)).toLocaleString()}
          </text>
        {/each}

        {#if activeIndex != null}
          <rect
            class="timeline-highlight"
            x={PAD_LEFT + step * activeIndex}
            y={PAD_TOP}
            width={Math.max(step, 2)}
            height={plotHeight}
          />
        {/if}

        {#each columns as column, index (column.startDate)}
          {@const x = columnCenter(index) - barWidth / 2}
          {@const accessedHeight = showAccessed ? barHeight(column.accessedCount) : 0}
          {@const completedHeight = showCompleted ? barHeight(column.completedCount) : 0}
          {@const skippedHeight = showSkipped ? barHeight(column.skippedCount) : 0}
          {#if accessedHeight > 0}
            <rect
              class="timeline-bar"
              x={x}
              y={PAD_TOP + plotHeight - accessedHeight}
              width={barWidth}
              height={accessedHeight}
              fill="url(#timeline-heat)"
            />
          {/if}
          {#if skippedHeight > 0}
            <rect
              class="timeline-bar timeline-bar-skipped"
              x={x + barWidth * 0.66}
              y={PAD_TOP + plotHeight - skippedHeight}
              width={Math.max(1, barWidth * 0.34)}
              height={skippedHeight}
            />
          {/if}
          {#if completedHeight > 0}
            <rect
              class="timeline-bar timeline-bar-completed"
              x={x + (accessesAreVisible ? barWidth * 0.33 : 0)}
              y={PAD_TOP + plotHeight - completedHeight}
              width={accessesAreVisible ? Math.max(1, barWidth * 0.34) : barWidth}
              height={completedHeight}
            />
          {/if}
        {/each}

        {#if trendPath}
          <path class="timeline-trend" d={trendPath} />
        {/if}

        <line
          class="timeline-baseline"
          x1={PAD_LEFT}
          y1={PAD_TOP + plotHeight}
          x2={width - PAD_RIGHT}
          y2={PAD_TOP + plotHeight}
        />

        {#each axisTicks as tick (tick.index)}
          <text
            class="timeline-axis-label"
            x={columnCenter(tick.index)}
            y={HEIGHT - 6}
            text-anchor="middle"
          >
            {tick.label}
          </text>
        {/each}
      </svg>
    {/if}
  </div>

  <div class="timeline-legend">
    {#if showAccessed}
      <span class="timeline-key"><span class="timeline-key-swatch timeline-key-accessed"></span>Opened</span>
    {/if}
    {#if showCompleted}
      <span class="timeline-key"><span class="timeline-key-swatch timeline-key-completed"></span>Completed</span>
    {/if}
    {#if showSkipped}
      <span class="timeline-key">
        <span
          class="timeline-key-swatch timeline-key-skipped"
        ></span>Skipped
      </span>
    {/if}
    {#if trendPath}
      <span class="timeline-key"><span class="timeline-key-line"></span>{trendLabel}</span>
    {/if}
    {#if groupSize > 1}
      <span class="timeline-key">One column per {granularityLabel}</span>
    {/if}
  </div>
</div>

<style>
  .timeline-readout {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 0.5rem;
    min-height: 1.15rem;
    margin: 0;
  }

  .timeline-readout-day {
    font-family: var(--font-heading);
    font-size: 0.85rem;
    font-weight: 600;
    color: var(--color-text-primary);
  }

  .timeline-readout-detail {
    font-family: var(--font-mono);
    font-size: 0.68rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-text-muted);
  }

  .timeline-frame {
    min-width: 0;
    border-radius: var(--radius-xs);
  }

  .timeline-frame:focus-visible {
    outline: 1px solid var(--color-border-accent-strong);
    outline-offset: 3px;
  }

  .timeline-svg {
    display: block;
    width: 100%;
    height: auto;
    touch-action: pan-y;
  }

  .timeline-grid {
    stroke: var(--color-border-subtle);
    stroke-width: 1;
  }

  .timeline-baseline {
    stroke: var(--color-border-default);
    stroke-width: 1;
  }

  .timeline-axis-label {
    fill: var(--color-text-disabled);
    font-family: var(--font-mono);
    font-size: 0.6rem;
  }

  .timeline-highlight {
    fill: rgb(255 255 255 / 0.06);
  }

  .timeline-bar {
    opacity: 0.88;
  }

  .timeline-bar-completed {
    fill: var(--color-text-muted);
    opacity: 0.82;
  }

  .timeline-bar-skipped {
    fill: var(--color-warning);
    opacity: 0.78;
  }

  .timeline-trend {
    fill: none;
    stroke: var(--color-accent-200);
    stroke-width: 1.4;
    stroke-linejoin: round;
    stroke-linecap: round;
    opacity: 0.75;
  }

  .timeline-legend {
    display: flex;
    flex-wrap: wrap;
    gap: 0.85rem;
  }

  .timeline-key {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    font-family: var(--font-mono);
    font-size: 0.64rem;
    color: var(--color-text-muted);
  }

  .timeline-key-swatch {
    width: 0.5rem;
    height: 0.5rem;
    border-radius: 1px;
  }

  .timeline-key-accessed {
    /* The swatch shows the whole ramp so the key doubles as the scale legend. */
    background: linear-gradient(0deg, var(--heat-ramp));
    opacity: 0.9;
  }

  .timeline-key-completed {
    background: var(--color-text-muted);
    opacity: 0.82;
  }

  .timeline-key-skipped {
    background: var(--color-warning);
    opacity: 0.78;
  }

  .timeline-key-line {
    width: 0.85rem;
    height: 0;
    border-top: 1.4px solid var(--color-accent-200);
    opacity: 0.75;
  }
</style>
