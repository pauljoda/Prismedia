<script lang="ts">
  import type { Snippet } from "svelte";

  interface Props {
    children: Snippet;
  }

  let { children }: Props = $props();
</script>

<div class="metadata-card-grid">
  {@render children()}
</div>

<style>
  .metadata-card-grid {
    display: flex;
    flex-wrap: wrap;
    align-items: start;
    gap: calc(var(--spacing) * 3);
  }

  .metadata-card-grid > :global(.metadata-card) {
    flex: 1 1 var(--spacing-metadata-card-min);
  }

  .metadata-card-grid > :global(:not(.metadata-card)) {
    flex: 1 1 100%;
    min-width: 0;
  }

  .metadata-card-grid > :global(.metadata-card-wide) {
    flex-grow: 2;
    flex-basis: calc(var(--spacing-metadata-card-min) * 2 + var(--spacing) * 3);
  }
</style>
