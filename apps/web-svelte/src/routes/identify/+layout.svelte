<script lang="ts">
  import { onDestroy } from "svelte";
  import {
    IdentifyStore,
    setIdentifyStore,
  } from "$lib/components/identify/identify-store.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  let { children } = $props();

  const nsfw = useNsfw();
  const store = new IdentifyStore(() => nsfw.mode === "off");
  setIdentifyStore(store);

  $effect(() => {
    void store.syncNsfwVisibility(nsfw.mode === "off");
  });

  onDestroy(() => {
    store.destroy();
  });
</script>

{@render children()}
