<script lang="ts">
  import type { Snippet } from "svelte";

  interface Props {
    title?: Snippet;
    breadcrumbs?: Snippet;
    children: Snippet;
  }

  let { title, breadcrumbs, children }: Props = $props();
</script>

<div class="space-y-4">
  {#if breadcrumbs}
    <div>{@render breadcrumbs()}</div>
  {/if}
  {#if title}
    <div class="space-y-1">
      <div>{@render title()}</div>
    </div>
  {/if}
  {@render children()}
</div>
