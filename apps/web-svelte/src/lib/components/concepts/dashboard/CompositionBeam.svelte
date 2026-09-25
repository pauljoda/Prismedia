<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import type { LibraryFamily } from "../library-composition";

  interface Props {
    families: LibraryFamily[];
    /** Line thickness in pixels. */
    thickness?: number;
    /** Fraction of the line spent as white light before it disperses. */
    source?: number;
    /** Draw the line in from the left once, on mount. */
    reveal?: boolean;
    class?: string;
  }

  let { families, thickness = 1, source = 0.3, reveal = true, class: className }: Props = $props();

  /** Smallest share a present family keeps, so a single comic is still a visible run of light. */
  const MIN_SHARE = 0.02;

  const present = $derived(families.filter((family) => family.count > 0));
  const shares = $derived.by(() => {
    const total = present.reduce((sum, family) => sum + family.count, 0);
    if (total === 0) return [];
    const raw = present.map((family) => Math.max(MIN_SHARE, family.count / total));
    const scale = raw.reduce((sum, share) => sum + share, 0);
    return raw.map((share) => share / scale);
  });
  const numberFormat = new Intl.NumberFormat();
  const description = $derived(
    present.map((family) => `${family.label} ${numberFormat.format(family.count)}`).join(", "),
  );
</script>

<!--
  A single hairline of light: white on the left, dispersing into one run per family on the right,
  each as long as that family's share of the library. It is the page's one literal light moment.
-->
<div
  class={cn("beam", reveal && "beam-reveal", className)}
  style:height="{thickness}px"
  role="img"
  aria-label={present.length > 0 ? `Library composition: ${description}` : "Library composition"}
>
  <span class="beam-source" style:flex-basis="{source * 100}%"></span>
  {#if present.length > 0}
    <span class="beam-spectrum" style:flex-basis="{(1 - source) * 100}%">
      {#each present as family, index (family.kind)}
        <span
          class="beam-band"
          style:flex-basis="{(shares[index] ?? 0) * 100}%"
          style:background="linear-gradient(90deg, {family.emitted.primary}, {family.emitted.secondary})"
        ></span>
      {/each}
    </span>
  {:else}
    <span class="beam-spectrum beam-empty" style:flex-basis="{(1 - source) * 100}%"></span>
  {/if}
</div>

<style>
  .beam {
    position: relative;
    display: flex;
    width: 100%;
    overflow: visible;
  }

  .beam-source {
    display: block;
    height: 100%;
    background: linear-gradient(
      90deg,
      transparent 0%,
      color-mix(in oklab, var(--color-text-primary) 30%, transparent) 55%,
      color-mix(in oklab, var(--color-text-primary) 85%, transparent) 100%
    );
  }

  .beam-spectrum {
    display: flex;
    height: 100%;
    gap: 1px;
  }

  .beam-band {
    display: block;
    height: 100%;
    opacity: 0.9;
  }

  .beam-empty {
    background: var(--color-border-default);
  }

  .beam-reveal {
    transform-origin: left center;
    animation: beam-draw 900ms var(--ease-default) both;
  }

  @keyframes beam-draw {
    from {
      transform: scaleX(0);
      opacity: 0.2;
    }
    to {
      transform: scaleX(1);
      opacity: 1;
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .beam-reveal {
      animation: none;
    }
  }
</style>
