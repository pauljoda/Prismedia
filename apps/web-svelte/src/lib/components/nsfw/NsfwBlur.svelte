<script lang="ts">
  import type { Snippet } from "svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  interface Props {
    isNsfw: boolean;
    class?: string;
    children: Snippet;
  }

  let { isNsfw, class: className, children }: Props = $props();
  const nsfw = useNsfw();
</script>

{#if !isNsfw || nsfw.mode === "show"}
  <div class={className}>{@render children()}</div>
{/if}
