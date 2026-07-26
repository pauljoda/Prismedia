<script lang="ts">
  import type { Component } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { PRISM_HEAT_STOPS } from "$lib/stats/prism-scale";

  interface Props {
    label: string;
    value: string;
    hint?: string;
    icon?: Component;
    /** Optional 0-1 fill rendered as a thin rail under the figure. */
    ratio?: number | null;
    /** Emphasizes the primary reading of the page. */
    emphasis?: boolean;
    class?: string;
  }

  let {
    label,
    value,
    hint,
    icon: Icon,
    ratio = null,
    emphasis = false,
    class: className,
  }: Props = $props();

  const clampedRatio = $derived(ratio == null ? null : Math.min(1, Math.max(0, ratio)));

  /**
   * The rail is a clipped window onto the full heat ramp, so a fuller rail lands on a warmer hue
   * exactly like a taller column in the activity chart or a busier cell in the rhythm grid.
   */
  const heatRamp = PRISM_HEAT_STOPS.map(
    (stop) => `${stop.color} ${(stop.offset * 100).toFixed(0)}%`,
  ).join(", ");
</script>

<div class={cn("stat-figure", emphasis && "stat-figure-emphasis", className)}>
  <div class="stat-figure-head">
    <span class="stat-figure-label">{label}</span>
    {#if Icon}
      <Icon class="h-3.5 w-3.5 shrink-0 text-text-disabled" aria-hidden="true" />
    {/if}
  </div>
  <div class="stat-figure-value">{value}</div>
  {#if hint}
    <div class="stat-figure-hint">{hint}</div>
  {/if}
  {#if clampedRatio != null}
    <div class="stat-figure-rail" aria-hidden="true">
      <span
        class="stat-figure-rail-fill"
        style:width={`${clampedRatio * 100}%`}
        style:--rail-ramp-width={`${(1 / Math.max(clampedRatio, 0.001)) * 100}%`}
        style:--heat-ramp={heatRamp}
      ></span>
    </div>
  {/if}
</div>

<style>
  .stat-figure {
    display: flex;
    flex-direction: column;
    gap: 0.3rem;
    min-width: 0;
    padding: 0.7rem 0.85rem 0.8rem;
  }

  .stat-figure-head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
  }

  .stat-figure-label {
    font-family: var(--font-mono);
    font-size: 0.6rem;
    font-weight: 600;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--color-text-muted);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .stat-figure-value {
    font-family: var(--font-heading);
    font-size: 1.5rem;
    font-weight: 600;
    line-height: 1.05;
    letter-spacing: -0.02em;
    font-variant-numeric: tabular-nums;
    color: var(--color-text-primary);
  }

  .stat-figure-emphasis .stat-figure-value {
    font-size: 2rem;
  }

  .stat-figure-hint {
    font-family: var(--font-mono);
    font-size: 0.64rem;
    font-variant-numeric: tabular-nums;
    line-height: 1.3;
    color: var(--color-text-disabled);
  }

  .stat-figure-rail {
    margin-top: 0.15rem;
    height: 2px;
    border-radius: 1px;
    background: var(--color-surface-3);
    overflow: hidden;
  }

  .stat-figure-rail-fill {
    display: block;
    height: 100%;
    /*
     * The ramp is sized to the full track and then clipped by the fill's own width, so the visible
     * end of the rail sits at the ramp position the value actually maps to.
     */
    background-image: linear-gradient(90deg, var(--heat-ramp));
    background-size: var(--rail-ramp-width) 100%;
    background-repeat: no-repeat;
    opacity: 0.9;
  }
</style>
