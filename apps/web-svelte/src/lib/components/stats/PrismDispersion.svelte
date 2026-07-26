<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import { PRISM_SPECTRUM } from "$lib/entities/entity-accent";
  import { entityKindIcon } from "$lib/entities/entity-kind-icons";
  import {
    formatWatchDuration,
    type PlaybackDispersionBand,
  } from "$lib/stats/playback-stats";

  interface Props {
    bands: PlaybackDispersionBand[];
    /** Family currently narrowing the page, drawn solid while the rest recede. */
    activeKind?: string | null;
    /** Called with the band's kind, or null when the active band is chosen again. */
    onSelect?: (kind: string | null) => void;
    class?: string;
  }

  let { bands, activeKind = null, onSelect, class: className }: Props = $props();

  /** Smallest readable band thickness, so a 1% family is still a visible line of light. */
  const MIN_BAND_THICKNESS = 7;
  const EDGE_PADDING = 9;

  let frameWidth = $state(0);
  let hoveredKind = $state<string | null>(null);

  // Geometry is computed in real pixels from the measured frame rather than through a scaled
  // viewBox, so the prism keeps the logo mark's proportions at every container width.
  const width = $derived(Math.max(frameWidth, 280));
  const height = $derived(Math.round(Math.min(236, Math.max(150, width * 0.31))));
  const centerY = $derived(height / 2);
  const prismX = $derived(width * 0.235);
  const prismHalf = $derived(Math.min(height * 0.34, 78));
  const prismWidth = $derived(prismHalf * 0.98);

  const apex = $derived({ x: prismX, y: centerY - prismHalf });
  const baseLeft = $derived({ x: prismX - prismWidth, y: centerY + prismHalf });
  const baseRight = $derived({ x: prismX + prismWidth, y: centerY + prismHalf });
  /**
   * The logo mark's three faces meet at an interior vertex below centre. Reproducing it here makes
   * the chart's prism read as the same object as the brand mark.
   */
  const facetCenter = $derived({ x: prismX, y: centerY + prismHalf * 0.22 });

  /** A point along the prism's right face, `t` running from the apex to the base. */
  const rightFacePoint = $derived((t: number) => ({
    x: prismX + prismWidth * t,
    y: centerY - prismHalf + 2 * prismHalf * t,
  }));

  const entryPoint = $derived({
    x: prismX - prismWidth * 0.52,
    y: centerY - prismHalf + 2 * prismHalf * 0.52,
  });

  const EXIT_FROM = 0.34;
  const EXIT_TO = 0.9;

  const face = $derived((...points: Array<{ x: number; y: number }>) =>
    `${points.map((point, index) => `${index === 0 ? "M" : "L"} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(" ")} Z`,
  );

  interface BandGeometry {
    band: PlaybackDispersionBand;
    path: string;
    gradientId: string;
    gradientX1: number;
    gradientX2: number;
  }

  const geometry = $derived.by<BandGeometry[]>(() => {
    if (bands.length === 0 || width <= 0) return [];

    const usable = Math.max(0, height - EDGE_PADDING * 2);
    const floor = Math.min(MIN_BAND_THICKNESS, usable / bands.length);
    const flexible = Math.max(0, usable - floor * bands.length);

    let endCursor = EDGE_PADDING;
    let exitCursor = EXIT_FROM;
    const exitSpan = EXIT_TO - EXIT_FROM;

    return bands.map((band, index) => {
      const thickness = floor + flexible * band.share;
      const endTop = endCursor;
      const endBottom = endCursor + thickness;
      endCursor = endBottom;

      const exitTop = rightFacePoint(exitCursor);
      exitCursor += exitSpan * (1 / bands.length);
      const exitBottom = rightFacePoint(exitCursor);

      const spread = width - exitTop.x;
      const control1 = exitTop.x + spread * 0.34;
      const control2 = exitTop.x + spread * 0.66;

      return {
        band,
        gradientId: `prism-band-${index}`,
        gradientX1: exitTop.x,
        gradientX2: width,
        path: [
          `M ${exitTop.x.toFixed(2)} ${exitTop.y.toFixed(2)}`,
          `C ${control1.toFixed(2)} ${exitTop.y.toFixed(2)} ${control2.toFixed(2)} ${endTop.toFixed(2)} ${width.toFixed(2)} ${endTop.toFixed(2)}`,
          `L ${width.toFixed(2)} ${endBottom.toFixed(2)}`,
          `C ${control2.toFixed(2)} ${endBottom.toFixed(2)} ${control1.toFixed(2)} ${exitBottom.y.toFixed(2)} ${exitBottom.x.toFixed(2)} ${exitBottom.y.toFixed(2)}`,
          "Z",
        ].join(" "),
      };
    });
  });

  const outlinePath = $derived(face(apex, baseRight, baseLeft));
  /** The pale glass face the incoming white light strikes, as on the logo mark. */
  const entryFacePath = $derived(face(apex, facetCenter, baseLeft));
  /** The face the separated light leaves through, tinted with the spectrum it carries. */
  const exitFacePath = $derived(face(apex, baseRight, facetCenter));
  /** The deep cool underside of the mark. */
  const baseFacePath = $derived(face(baseLeft, facetCenter, baseRight));

  /**
   * Emphasis follows the pointer first and the active filter second, so hovering a band always
   * previews it even while a different family is filtered in.
   */
  function bandOpacity(kind: string): number {
    if (hoveredKind) return hoveredKind === kind ? 1 : 0.16;
    if (activeKind) return activeKind === kind ? 1 : 0.24;
    return 0.9;
  }

  function shareLabel(share: number): string {
    const percent = share * 100;
    if (percent > 0 && percent < 1) return "<1%";
    return `${Math.round(percent)}%`;
  }

  function select(kind: string) {
    onSelect?.(activeKind === kind ? null : kind);
  }
</script>

<div class={cn("grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(15rem,19rem)]", className)}>
  <!--
    The dispersion drawing is a visual restatement of the legend beside it, so it is hidden from
    assistive technology and the legend buttons carry the real semantics and keyboard access.
  -->
  <div class="prism-frame" bind:clientWidth={frameWidth} aria-hidden="true">
    {#if frameWidth > 0}
      <svg
        class="prism-svg"
        width={width}
        height={height}
        viewBox={`0 0 ${width} ${height}`}
        role="presentation"
      >
        <defs>
          <linearGradient id="prism-beam" x1="0" y1="0" x2={entryPoint.x} y2="0" gradientUnits="userSpaceOnUse">
            <stop offset="0" stop-color="#ffffff" stop-opacity="0" />
            <stop offset="0.4" stop-color="#e8f4ff" stop-opacity="0.6" />
            <stop offset="1" stop-color="#ffffff" stop-opacity="1" />
          </linearGradient>
          <!-- The logo's entry face: white light turning pale blue through the glass. -->
          <linearGradient id="prism-entry-face" x1={apex.x} y1={apex.y} x2={baseLeft.x} y2={baseLeft.y} gradientUnits="userSpaceOnUse">
            <stop offset="0" stop-color="#ffffff" stop-opacity="0.9" />
            <stop offset="0.55" stop-color="#dceeff" stop-opacity="0.62" />
            <stop offset="1" stop-color="#9dc9ee" stop-opacity="0.4" />
          </linearGradient>
          <linearGradient id="prism-exit-face" x1="0" y1={apex.y} x2="0" y2={baseRight.y} gradientUnits="userSpaceOnUse">
            {#each bands as band, index (band.kind)}
              <stop
                offset={bands.length === 1 ? index : index / (bands.length - 1)}
                stop-color={band.emitted.primary}
                stop-opacity="0.46"
              />
            {/each}
          </linearGradient>
          <linearGradient id="prism-base-face" x1={baseLeft.x} y1="0" x2={baseRight.x} y2="0" gradientUnits="userSpaceOnUse">
            <stop offset="0" stop-color={PRISM_SPECTRUM.blue} stop-opacity="0.34" />
            <stop offset="0.55" stop-color={PRISM_SPECTRUM.violet} stop-opacity="0.26" />
            <stop offset="1" stop-color={PRISM_SPECTRUM.magenta} stop-opacity="0.32" />
          </linearGradient>
          {#each geometry as item (item.gradientId)}
            <linearGradient
              id={item.gradientId}
              x1={item.gradientX1}
              y1="0"
              x2={item.gradientX2}
              y2="0"
              gradientUnits="userSpaceOnUse"
            >
              <stop offset="0" stop-color={item.band.emitted.primary} stop-opacity="0.5" />
              <stop offset="0.45" stop-color={item.band.emitted.primary} stop-opacity="0.9" />
              <stop offset="1" stop-color={item.band.emitted.secondary} stop-opacity="0.86" />
            </linearGradient>
          {/each}
        </defs>

        <line
          class="prism-beam-bloom"
          x1="0"
          y1={entryPoint.y}
          x2={entryPoint.x}
          y2={entryPoint.y}
          stroke="url(#prism-beam)"
        />
        <line
          class="prism-beam"
          x1="0"
          y1={entryPoint.y}
          x2={entryPoint.x}
          y2={entryPoint.y}
          stroke="url(#prism-beam)"
        />

        <g class="prism-bands">
          {#each geometry as item (item.gradientId)}
            <path
              class="prism-band"
              d={item.path}
              fill={`url(#${item.gradientId})`}
              opacity={bandOpacity(item.band.kind)}
            />
          {/each}
          <!-- Hairline seams keep each family readable inside what is otherwise one smooth beam. -->
          {#each geometry as item (item.gradientId)}
            <path class="prism-band-seam" d={item.path} opacity={bandOpacity(item.band.kind)} />
          {/each}
        </g>

        <g class="prism-body">
          <path class="prism-face" d={baseFacePath} fill="url(#prism-base-face)" />
          <path class="prism-face" d={exitFacePath} fill="url(#prism-exit-face)" />
          <path class="prism-face" d={entryFacePath} fill="url(#prism-entry-face)" />
          <path class="prism-facet-edge" d={`M ${apex.x} ${apex.y} L ${facetCenter.x} ${facetCenter.y}`} />
          <path class="prism-facet-edge" d={`M ${facetCenter.x} ${facetCenter.y} L ${baseLeft.x} ${baseLeft.y}`} />
          <path class="prism-facet-edge" d={`M ${facetCenter.x} ${facetCenter.y} L ${baseRight.x} ${baseRight.y}`} />
          <path class="prism-edge" d={outlinePath} />
        </g>
      </svg>
    {/if}
  </div>

  <ul class="dispersion-legend">
    {#each bands as band (band.kind)}
      {@const Icon = entityKindIcon(band.kind)}
      {@const isActive = activeKind === band.kind}
      <li>
        <button
          type="button"
          class={cn("legend-row", isActive && "legend-row-active")}
          style:--band-primary={band.accent.primary}
          style:--band-secondary={band.accent.secondary}
          aria-pressed={isActive}
          onclick={() => select(band.kind)}
          onpointerenter={() => (hoveredKind = band.kind)}
          onpointerleave={() => (hoveredKind = null)}
          onfocus={() => (hoveredKind = band.kind)}
          onblur={() => (hoveredKind = null)}
        >
          <span class="legend-rail" aria-hidden="true"></span>
          <Icon class="h-3.5 w-3.5 shrink-0 text-text-muted" aria-hidden="true" />
          <span class="legend-label">{band.label}</span>
          <span class="legend-share">{shareLabel(band.share)}</span>
          <span class="legend-detail">
            {band.totalEvents.toLocaleString()} · {formatWatchDuration(band.watchSeconds)}
          </span>
        </button>
      </li>
    {/each}
  </ul>
</div>

<style>
  .prism-frame {
    position: relative;
    min-width: 0;
    display: flex;
    align-items: center;
    overflow: hidden;
    border-radius: var(--radius-sm);
    background:
      radial-gradient(ellipse 55% 120% at 22% 50%, rgb(255 255 255 / 0.035), transparent 70%),
      var(--color-surface-1);
  }

  .prism-svg {
    display: block;
    width: 100%;
    height: auto;
  }

  /*
   * The beam is drawn twice: a wide soft pass for the bloom and a tight bright pass for the core.
   * A literal prism moment is one of the few places the language allows emitted light.
   */
  .prism-beam-bloom {
    stroke-width: 7;
    opacity: 0.16;
    filter: blur(3px);
  }

  .prism-beam {
    stroke-width: 1.6;
    filter: drop-shadow(0 0 5px rgb(232 244 255 / 0.85));
  }

  .prism-face {
    stroke: none;
  }

  /* Bright hairline edges are what read the shape as cut glass on the brand mark. */
  .prism-edge {
    fill: none;
    stroke: rgb(226 244 255 / 0.72);
    stroke-width: 1;
    stroke-linejoin: round;
  }

  .prism-facet-edge {
    fill: none;
    stroke: rgb(226 244 255 / 0.3);
    stroke-width: 1;
  }

  .prism-band {
    transition: opacity var(--duration-normal, 200ms) var(--ease-default, ease);
  }

  .prism-band-seam {
    fill: none;
    stroke: var(--color-bg);
    stroke-width: 1;
    transition: opacity var(--duration-normal, 200ms) var(--ease-default, ease);
  }

  .prism-bands {
    transform-origin: left center;
    animation: prism-disperse 620ms var(--ease-default, cubic-bezier(0.25, 0, 0.25, 1)) both;
  }

  @keyframes prism-disperse {
    from {
      transform: scaleX(0.82);
      opacity: 0;
    }
    to {
      transform: scaleX(1);
      opacity: 1;
    }
  }

  .dispersion-legend {
    display: flex;
    flex-direction: column;
    gap: 2px;
    margin: 0;
    padding: 0;
    list-style: none;
    min-width: 0;
  }

  .legend-row {
    display: grid;
    grid-template-columns: 3px auto minmax(0, 1fr) auto;
    grid-template-rows: auto auto;
    align-items: center;
    gap: 0 0.5rem;
    width: 100%;
    padding: 0.4rem 0.55rem;
    border: 1px solid transparent;
    border-radius: var(--radius-xs);
    background: transparent;
    text-align: left;
    cursor: pointer;
    transition:
      background var(--duration-fast, 120ms) var(--ease-default, ease),
      border-color var(--duration-fast, 120ms) var(--ease-default, ease);
  }

  .legend-row:hover,
  .legend-row:focus-visible {
    background: var(--color-surface-2);
    border-color: var(--color-border-subtle);
    outline: none;
  }

  .legend-row:focus-visible {
    border-color: var(--color-border-accent);
  }

  .legend-row-active {
    background: var(--color-surface-2);
    border-color: var(--color-border-default);
  }

  .legend-rail {
    grid-row: 1 / span 2;
    align-self: stretch;
    min-height: 1.6rem;
    border-radius: 2px;
    background: linear-gradient(180deg, var(--band-primary), var(--band-secondary));
    opacity: 0.85;
    transition: opacity var(--duration-fast, 120ms) var(--ease-default, ease);
  }

  .legend-row:hover .legend-rail,
  .legend-row:focus-visible .legend-rail,
  .legend-row-active .legend-rail {
    opacity: 1;
  }

  .legend-label {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.8rem;
    color: var(--color-text-secondary);
  }

  .legend-row-active .legend-label,
  .legend-row:hover .legend-label {
    color: var(--color-text-primary);
  }

  .legend-share {
    font-family: var(--font-mono);
    font-size: 0.74rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-text-primary);
  }

  .legend-detail {
    grid-column: 3 / span 2;
    font-family: var(--font-mono);
    font-size: 0.66rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-text-disabled);
  }

  @media (prefers-reduced-motion: reduce) {
    .prism-bands {
      animation: none;
    }

    .prism-band,
    .prism-band-seam,
    .legend-row,
    .legend-rail {
      transition: none;
    }
  }
</style>
