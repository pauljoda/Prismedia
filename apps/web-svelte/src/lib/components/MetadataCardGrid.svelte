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
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 14rem), 1fr));
    gap: 0.5rem;
  }

  .metadata-card-grid > :global(:not(.metadata-card)) {
    grid-column: 1 / -1;
  }
</style>
