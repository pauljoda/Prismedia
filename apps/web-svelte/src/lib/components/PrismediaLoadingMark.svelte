<script lang="ts">
  import type { Attachment } from "svelte/attachments";
  import {
    PRISM_LOADING_CYCLE_SECONDS,
    interpolate,
    interpolatePrismLoadingPoint,
    prismLoadingBandPath,
    prismLoadingFrameAt,
    prismLoadingGeometry,
    prismLoadingMetrics,
    prismLoadingSpectrumStart,
    prismLoadingSpectrumTarget,
    reducedMotionPrismLoadingFrame,
  } from "./prism-loading-frame";

  interface Props {
    label?: string;
    compact?: boolean;
    showLabel?: boolean;
    markSize?: number;
    height?: number;
    active?: boolean;
    previewProgress?: number;
    class?: string;
  }

  const spectrumColors = [
    { id: "red", color: "var(--color-spectrum-red, #ff141f)" },
    { id: "orange", color: "var(--color-spectrum-orange, #ff570a)" },
    { id: "yellow", color: "var(--color-spectrum-yellow, #ffc71f)" },
    { id: "green", color: "var(--color-spectrum-green, #1fc247)" },
    { id: "cyan", color: "var(--color-spectrum-cyan, #0ab3e6)" },
    { id: "blue", color: "var(--color-spectrum-blue, #0d47ff)" },
    { id: "violet", color: "var(--color-spectrum-violet, #7a14f5)" },
  ] as const;

  let {
    label = "Loading",
    compact = false,
    showLabel = false,
    markSize,
    height,
    active = true,
    previewProgress,
    class: className,
  }: Props = $props();

  const instanceId = $props.id();
  const incomingGlowId = `${instanceId}-incoming-glow`;
  const incomingGradientId = `${instanceId}-incoming-gradient`;
  const spectrumGlowId = `${instanceId}-spectrum-glow`;
  const impactGradientId = `${instanceId}-impact-gradient`;

  let viewport = $state.raw({ width: 760, height: 128 });
  let animationProgress = $state(0);
  let reducedMotion = $state(false);

  const effectiveHeight = $derived(height ?? (compact ? 72 : 128));
  const effectiveMarkSize = $derived(markSize ?? (compact ? 48 : 72));
  const frame = $derived(
    reducedMotion
      ? reducedMotionPrismLoadingFrame
      : prismLoadingFrameAt(previewProgress ?? animationProgress),
  );
  const geometry = $derived(
    prismLoadingGeometry(viewport.width, viewport.height, effectiveMarkSize),
  );
  const incomingHead = $derived(
    interpolatePrismLoadingPoint(
      geometry.incomingStart,
      geometry.entry,
      frame.incomingLightProgress,
    ),
  );
  const incomingPath = $derived(
    `M ${geometry.incomingStart.x} ${geometry.incomingStart.y} L ${incomingHead.x} ${incomingHead.y}`,
  );
  const internalLightProgress = $derived(
    Math.min(
      Math.max(
        (frame.incomingLightProgress - prismLoadingMetrics.internalLightStart) /
          prismLoadingMetrics.internalLightLength,
        0,
      ),
      1,
    ),
  );
  const internalLightEnd = $derived(
    interpolatePrismLoadingPoint(geometry.entry, geometry.impact, internalLightProgress),
  );
  const internalLightPath = $derived(
    `M ${geometry.entry.x} ${geometry.entry.y} L ${internalLightEnd.x} ${internalLightEnd.y}`,
  );
  const internalLightOpacity = $derived(
    frame.incomingLightOpacity * internalLightProgress,
  );
  const spectrumBands = $derived.by(() =>
    spectrumColors.map((spectrumColor, index) => {
      const fraction = index / (spectrumColors.length - 1);
      const start = prismLoadingSpectrumStart(geometry, fraction);
      const target = prismLoadingSpectrumTarget(geometry, fraction);
      const end = interpolatePrismLoadingPoint(start, target, frame.spectrumProgress);
      const terminalWidth = interpolate(
        prismLoadingMetrics.lightLineWidth,
        prismLoadingMetrics.spectrumBandWidth,
        frame.spectrumProgress,
      );

      return {
        ...spectrumColor,
        start,
        end,
        path: prismLoadingBandPath(
          start,
          end,
          prismLoadingMetrics.lightLineWidth,
          terminalWidth,
        ),
        gradientId: `${instanceId}-spectrum-${spectrumColor.id}`,
      };
    }),
  );

  function animate(
    isActive: boolean,
    fixedProgress: number | undefined,
    fallbackHeight: number,
  ): Attachment<HTMLElement> {
    return (element) => {
      let animationFrame = 0;
      let startTime = performance.now();
      const motionPreference = window.matchMedia("(prefers-reduced-motion: reduce)");

      const measure = (width: number, measuredHeight: number) => {
        viewport = {
          width: width || 760,
          height: measuredHeight || fallbackHeight,
        };
      };
      const resizeObserver = new ResizeObserver(([entry]) => {
        if (!entry) return;
        measure(entry.contentRect.width, entry.contentRect.height);
      });
      const stopAnimation = () => {
        cancelAnimationFrame(animationFrame);
        animationFrame = 0;
      };
      const tick = (time: number) => {
        const elapsedSeconds = (time - startTime) / 1_000;
        animationProgress =
          (elapsedSeconds % PRISM_LOADING_CYCLE_SECONDS) / PRISM_LOADING_CYCLE_SECONDS;
        animationFrame = requestAnimationFrame(tick);
      };
      const synchronizeMotion = () => {
        stopAnimation();
        reducedMotion = motionPreference.matches;
        if (reducedMotion || !isActive || fixedProgress !== undefined) return;
        startTime = performance.now() - animationProgress * PRISM_LOADING_CYCLE_SECONDS * 1_000;
        animationFrame = requestAnimationFrame(tick);
      };

      const bounds = element.getBoundingClientRect();
      measure(bounds.width, bounds.height);
      resizeObserver.observe(element);
      motionPreference.addEventListener("change", synchronizeMotion);
      synchronizeMotion();

      return () => {
        stopAnimation();
        resizeObserver.disconnect();
        motionPreference.removeEventListener("change", synchronizeMotion);
      };
    };
  }
</script>

<div
  class={["loading-mark", compact && "compact", className]}
  role="status"
  aria-label={label}
  aria-live="polite"
  aria-atomic="true"
  aria-busy="true"
>
  <div
    class="animation-surface"
    style:--loading-height={`${effectiveHeight}px`}
    {@attach animate(active, previewProgress, effectiveHeight)}
  >
    <svg
      viewBox={`0 0 ${viewport.width} ${viewport.height}`}
      preserveAspectRatio="none"
      color-interpolation="linearRGB"
      aria-hidden="true"
      focusable="false"
      data-reduced-motion={reducedMotion}
    >
      <defs>
        <linearGradient
          id={incomingGradientId}
          gradientUnits="userSpaceOnUse"
          x1={geometry.incomingStart.x}
          y1={geometry.incomingStart.y}
          x2={incomingHead.x}
          y2={incomingHead.y}
        >
          <stop offset="0" stop-color="#fff" stop-opacity="0" />
          <stop offset="0.62" stop-color="#fff" stop-opacity="0.58" />
          <stop offset="1" stop-color="#fff" />
        </linearGradient>
        <filter id={incomingGlowId} x="-30%" y="-100%" width="160%" height="300%">
          <feGaussianBlur stdDeviation={prismLoadingMetrics.beamGlowRadius} />
        </filter>
        <filter id={spectrumGlowId} x="-15%" y="-100%" width="130%" height="300%">
          <feGaussianBlur stdDeviation={prismLoadingMetrics.spectrumGlowRadius} />
        </filter>
        <radialGradient id={impactGradientId} cx="50%" cy="50%" r="50%">
          <stop offset="0" stop-color="#fff" />
          <stop offset="0.34" stop-color="var(--color-spectrum-yellow, #ffc71f)" stop-opacity="0.42" />
          <stop offset="1" stop-color="#fff" stop-opacity="0" />
        </radialGradient>
        {#each spectrumBands as band (band.id)}
          <linearGradient
            id={band.gradientId}
            gradientUnits="userSpaceOnUse"
            x1={band.start.x}
            y1={band.start.y}
            x2={band.end.x}
            y2={band.end.y}
          >
            <stop offset="0" stop-color={band.color} />
            <stop offset="1" stop-color={band.color} stop-opacity="0.72" />
          </linearGradient>
        {/each}
      </defs>

      <g class="spectrum" opacity={frame.spectrumOpacity}>
        {#each spectrumBands as band (band.id)}
          <path
            class="spectrum-band spectrum-band-glow"
            d={band.path}
            fill={band.color}
            fill-opacity="0.64"
            filter={`url(#${spectrumGlowId})`}
          />
          <path class="spectrum-band" d={band.path} fill={`url(#${band.gradientId})`} />
        {/each}
      </g>

      <g class="incoming-light" opacity={frame.incomingLightOpacity}>
        <path
          d={incomingPath}
          fill="none"
          stroke={`url(#${incomingGradientId})`}
          stroke-width={prismLoadingMetrics.lightLineWidth * 2}
          filter={`url(#${incomingGlowId})`}
          class="light-glow"
        />
        <path
          d={incomingPath}
          fill="none"
          stroke={`url(#${incomingGradientId})`}
          stroke-width={prismLoadingMetrics.lightLineWidth}
        />
        <circle
          cx={incomingHead.x}
          cy={incomingHead.y}
          r={prismLoadingMetrics.lightLineWidth * 1.8}
          fill="#fff"
        />
      </g>

      <image
        class="prism-neutral"
        href="/brand/prismedia-prism-neutral.png"
        x={geometry.prism.x}
        y={geometry.prism.y}
        width={geometry.prism.width}
        height={geometry.prism.height}
        preserveAspectRatio="none"
      />
      <image
        class="prism-color"
        href="/brand/prismedia-prism-color.png"
        x={geometry.prism.x}
        y={geometry.prism.y}
        width={geometry.prism.width}
        height={geometry.prism.height}
        preserveAspectRatio="none"
        opacity={frame.prismColorProgress}
      />

      <g opacity={internalLightOpacity}>
        <path
          d={internalLightPath}
          fill="none"
          stroke="#fff"
          stroke-width={prismLoadingMetrics.lightLineWidth * 2}
          filter={`url(#${incomingGlowId})`}
          class="light-glow"
        />
        <path
          d={internalLightPath}
          fill="none"
          stroke="#fff"
          stroke-width={prismLoadingMetrics.lightLineWidth}
        />
      </g>

      <circle
        class="impact-glow"
        cx={geometry.impact.x}
        cy={geometry.impact.y}
        r={prismLoadingMetrics.impactGlowRadius}
        fill={`url(#${impactGradientId})`}
        opacity={frame.impactGlowOpacity}
      />
    </svg>
  </div>
  {#if showLabel}
    <span class="label">{label}</span>
  {/if}
</div>

<style>
  .loading-mark {
    display: grid;
    width: 100%;
    min-width: 0;
    place-items: center;
    gap: 0.5rem;
  }

  .animation-surface {
    width: 100%;
    height: var(--loading-height);
    min-width: 0;
    overflow: hidden;
    isolation: isolate;
    background: transparent;
  }

  svg {
    display: block;
    width: 100%;
    height: 100%;
    overflow: hidden;
  }

  .light-glow,
  .spectrum-band-glow {
    mix-blend-mode: screen;
  }

  .label {
    color: var(--color-text-muted);
    font-family: var(--font-heading);
    font-size: 0.8rem;
    letter-spacing: 0.02em;
  }

</style>
