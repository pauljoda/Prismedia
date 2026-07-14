<script lang="ts">
  import PrismediaLoadingMark from "$lib/components/PrismediaLoadingMark.svelte";
  import type { EntityDetailPosterSize } from "./EntityDetail.svelte";

  interface Props {
    posterSize?: EntityDetailPosterSize;
    showHero?: boolean;
    tabCount?: number;
    /** CSS aspect-ratio retained for route compatibility while loading. */
    posterAspect?: string;
  }

  let {
    posterSize = "large",
    showHero = true,
    tabCount = 3,
    posterAspect = "2 / 3",
  }: Props = $props();
</script>

<article
  class="detail-loading"
  data-poster-size={posterSize}
  data-show-hero={showHero}
  data-tab-count={tabCount}
  style:--skeleton-poster-aspect={posterAspect}
  aria-busy="true"
  aria-label="Loading entity"
>
  <PrismediaLoadingMark label="Loading entity" showLabel />
</article>

<style>
  .detail-loading {
    display: grid;
    min-height: clamp(18rem, 48vh, 32rem);
    place-items: center;
    overflow: hidden;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-lg);
    background: rgb(0 0 0 / 0.42);
  }
</style>
