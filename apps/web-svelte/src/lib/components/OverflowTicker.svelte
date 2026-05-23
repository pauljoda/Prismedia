<script lang="ts">
  import { onMount, tick } from "svelte";

  interface Props {
    align?: "left" | "center" | "right";
    class?: string;
    minScale?: number;
    scaleToFit?: boolean;
    text: string;
    title?: string;
  }

  let {
    align = "left",
    class: className = "",
    minScale = 0.72,
    scaleToFit = false,
    text,
    title,
  }: Props = $props();

  let shell: HTMLSpanElement | null = $state(null);
  let measureNode: HTMLSpanElement | null = $state(null);
  let overflowing = $state(false);
  let scale = $state(1);
  let travel = $state(0);

  const boundedMinScale = $derived(Math.max(0.5, Math.min(1, minScale)));
  const duration = $derived(`${Math.max(5, Math.min(14, travel / 18 + 5))}s`);
  const displayTitle = $derived(title ?? text);

  function measure() {
    if (!shell || !measureNode) return;
    const shellWidth = shell.clientWidth;
    const textWidth = measureNode.scrollWidth;
    if (shellWidth <= 0 || textWidth <= 0) return;
    const nextScale = scaleToFit && textWidth > shellWidth
      ? Math.max(boundedMinScale, Math.min(1, shellWidth / textWidth))
      : 1;
    const scaledTextWidth = textWidth * nextScale;
    const nextTravel = Math.max(0, Math.ceil(scaledTextWidth - shellWidth + 4));
    scale = nextScale;
    travel = nextTravel;
    overflowing = nextTravel > 1;
  }

  function scheduleMeasure() {
    if (typeof requestAnimationFrame === "undefined") {
      measure();
      return;
    }
    requestAnimationFrame(() => requestAnimationFrame(measure));
  }

  onMount(() => {
    scheduleMeasure();
    void document.fonts?.ready.then(scheduleMeasure);
    if (typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(scheduleMeasure);
    if (shell) observer.observe(shell);
    if (measureNode) observer.observe(measureNode);
    return () => observer.disconnect();
  });

  $effect(() => {
    text;
    void tick().then(scheduleMeasure);
  });
</script>

<span
  bind:this={shell}
  class={`ticker-shell${overflowing ? " is-overflowing" : ""}${scale < 0.999 ? " is-scaled" : ""}${className ? ` ${className}` : ""}`}
  data-align={align}
  title={displayTitle}
  style:--ticker-align={align}
  style:--ticker-scale={scale}
  style:--ticker-travel={`${travel}px`}
  style:--ticker-duration={duration}
>
  <span class="ticker-track">{text}</span>
  <span bind:this={measureNode} class="ticker-measure" aria-hidden="true">{text}</span>
</span>

<style>
  .ticker-shell {
    position: relative;
    display: block;
    min-width: 0;
    max-width: 100%;
    overflow: hidden;
    text-align: var(--ticker-align, left);
    white-space: nowrap;
  }

  .ticker-track {
    display: inline-block;
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    transform: scale(var(--ticker-scale, 1));
    transform-origin: left center;
    vertical-align: bottom;
    white-space: nowrap;
  }

  .ticker-measure {
    position: absolute;
    inset: 0 auto auto 0;
    display: inline-block;
    min-width: max-content;
    visibility: hidden;
    white-space: nowrap;
    pointer-events: none;
  }

  .ticker-shell:is(.is-overflowing, .is-scaled) .ticker-track {
    max-width: none;
  }

  .ticker-shell.is-overflowing {
    text-align: left;
  }

  .ticker-shell:not(.is-overflowing)[data-align="center"] .ticker-track {
    transform-origin: center;
  }

  .ticker-shell:not(.is-overflowing)[data-align="right"] .ticker-track {
    transform-origin: right center;
  }

  .ticker-shell.is-overflowing:is(:hover, :focus-visible, :focus-within) .ticker-track {
    animation: overflow-ticker var(--ticker-duration, 7s) linear infinite alternate;
  }

  @keyframes overflow-ticker {
    0%, 12% {
      transform: translateX(0) scale(var(--ticker-scale, 1));
    }

    88%, 100% {
      transform: translateX(calc(-1 * var(--ticker-travel, 0px))) scale(var(--ticker-scale, 1));
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .ticker-shell.is-overflowing:is(:hover, :focus-visible, :focus-within) .ticker-track {
      animation: none;
    }
  }
</style>
