<script lang="ts">
  import type { Snippet } from "svelte";
  import { onMount } from "svelte";
  import { ArrowLeft, X } from "@lucide/svelte";
  import { fade } from "svelte/transition";
  import { dur, ease } from "@prismedia/ui-svelte";
  import { createNavigationKeyHandler } from "$lib/keyboard/navigation-keyboard";
  import { portal } from "$lib/actions/portal";

  interface Props {
    title?: string;
    presentation?: "overlay" | "page";
    closeIcon?: "close" | "back";
    onClose: () => void;
    /** Optional keyboard navigation hooks shared across reader formats. */
    onPrev?: () => void;
    onNext?: () => void;
    onActivate?: () => void;
    /** Counter / position readout rendered next to the title. */
    counter?: Snippet;
    /** Format-specific toggles rendered on the right of the top bar. */
    controls?: Snippet;
    /** The reader stage. Fills the overlay beneath the floating chrome. */
    children: Snippet;
  }

  let {
    title = "Reader",
    presentation = "overlay",
    closeIcon = "close",
    onClose,
    onPrev,
    onNext,
    onActivate,
    counter,
    controls,
    children,
  }: Props = $props();

  let controlsVisible = $state(true);
  let controlsTimer: number | null = null;

  const closeLabel = $derived(closeIcon === "back" ? "Back" : "Close");
  const closeTitle = $derived(closeIcon === "back" ? "Back (Esc)" : "Close (Esc)");

  function clearControlsTimer() {
    if (!controlsTimer) return;
    window.clearTimeout(controlsTimer);
    controlsTimer = null;
  }

  /** Reveal the chrome and schedule it to auto-hide. Exposed to reader bodies. */
  export function showControls() {
    controlsVisible = true;
    clearControlsTimer();
    controlsTimer = window.setTimeout(() => {
      controlsVisible = false;
      controlsTimer = null;
    }, 2800);
  }

  /** Toggle the chrome on a centre tap. Exposed to reader bodies. */
  export function toggleControls() {
    if (controlsVisible) {
      controlsVisible = false;
      clearControlsTimer();
    } else {
      showControls();
    }
  }

  onMount(() => {
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const onKey = createNavigationKeyHandler({
      close: onClose,
      prev: () => onPrev?.(),
      next: () => onNext?.(),
      extraKeys: onActivate ? { " ": () => onActivate() } : undefined,
    });

    window.addEventListener("keydown", onKey);
    showControls();
    return () => {
      window.removeEventListener("keydown", onKey);
      document.body.style.overflow = prevOverflow;
      clearControlsTimer();
    };
  });
</script>

<div
  use:portal
  data-reader-overlay
  class={`reader-overlay fixed inset-0 flex flex-col bg-black backdrop-blur-sm ${presentation === "page" ? "reader-page-presentation" : ""}`}
  role="dialog"
  aria-modal="true"
  in:fade={{ duration: dur.normal, easing: ease.enter }}
  out:fade={{ duration: dur.fast, easing: ease.exit }}
>
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    data-reader-hover-zone="top"
    class="reader-hover-zone reader-hover-zone-top"
    onpointerenter={showControls}
  ></div>
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    data-reader-hover-zone="bottom"
    class="reader-hover-zone reader-hover-zone-bottom"
    onpointerenter={showControls}
  ></div>

  <div
    data-reader-control
    class={`reader-top-layer ${controlsVisible ? "reader-layer-visible" : "reader-layer-hidden"}`}
  >
    <button
      type="button"
      onclick={onClose}
      class="reader-icon-button"
      aria-label={closeLabel}
      title={closeTitle}
    >
      {#if closeIcon === "back"}
        <ArrowLeft class="h-5 w-5" />
      {:else}
        <X class="h-5 w-5" />
      {/if}
    </button>

    <div class="min-w-0 flex-1">
      <h2 class="truncate text-sm font-medium text-text-primary">{title}</h2>
      {#if counter}
        <div class="font-mono text-[0.6rem] uppercase tracking-[0.14em] text-text-muted">
          {@render counter()}
        </div>
      {/if}
    </div>

    {#if controls}
      {@render controls()}
    {/if}
  </div>

  {@render children()}
</div>

<style>
  .reader-icon-button {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    border: 1px solid var(--color-border-default);
    background: var(--color-overlay-heavy);
    padding: 0.4rem;
    border-radius: var(--radius-sm);
    color: var(--color-text-secondary);
    font-size: 0.72rem;
    line-height: 1;
    backdrop-filter: blur(var(--glass-blur-sm));
    transition:
      border-color var(--duration-normal) var(--ease-mechanical),
      color var(--duration-normal) var(--ease-mechanical),
      box-shadow var(--duration-normal) var(--ease-mechanical);
  }

  .reader-icon-button:hover,
  .reader-icon-button:focus-visible {
    border-color: var(--color-border-accent-strong);
    color: var(--color-text-accent-bright);
    box-shadow: var(--shadow-glow-accent);
    outline: none;
  }

  .reader-overlay {
    z-index: 2147483000;
    width: 100vw;
    height: 100vh;
    min-height: 100vh;
    overflow: hidden;
  }

  @supports (height: 100lvh) {
    .reader-overlay {
      height: 100lvh;
      min-height: 100lvh;
    }
  }

  .reader-hover-zone {
    position: absolute;
    left: 0;
    right: 0;
    z-index: 15;
    height: 5rem;
  }

  .reader-hover-zone-top {
    top: 0;
  }

  .reader-hover-zone-bottom {
    bottom: 0;
  }

  .reader-top-layer {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    z-index: 20;
    display: flex;
    align-items: center;
    gap: 0.5rem;
    border-color: var(--color-border-default);
    background: linear-gradient(
      to bottom,
      var(--color-overlay-heavy),
      rgba(7, 8, 11, 0.48) 68%,
      transparent
    );
    padding: max(0.5rem, env(safe-area-inset-top)) 0.75rem 1.25rem;
    backdrop-filter: blur(var(--glass-blur-sm));
    transition:
      opacity var(--duration-normal) var(--ease-mechanical),
      transform var(--duration-normal) var(--ease-mechanical);
  }

  .reader-layer-visible {
    opacity: 1;
    pointer-events: auto;
    transform: translateY(0);
  }

  .reader-layer-hidden {
    opacity: 0;
    pointer-events: none;
    transform: translateY(-0.75rem);
  }

  @media (min-width: 640px) {
    .reader-top-layer {
      border-bottom: 1px solid var(--color-border-default);
      background: var(--color-overlay-glass);
      padding: 0.5rem 0.75rem;
    }
  }
</style>
