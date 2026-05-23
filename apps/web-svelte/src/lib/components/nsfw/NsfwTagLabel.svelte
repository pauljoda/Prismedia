<script lang="ts">
  import { useNsfw } from "$lib/nsfw/store.svelte";

  interface Props {
    isNsfw: boolean;
    text: string;
    class?: string;
  }

  let { isNsfw, text, class: className }: Props = $props();
  const nsfw = useNsfw();
  const tagIsNsfw = $derived(isNsfw === true);
</script>

{#if !tagIsNsfw || nsfw.mode === "show"}
  <span class={className}>{text}</span>
{/if}
