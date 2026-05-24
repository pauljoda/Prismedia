<script lang="ts">
  import { onDestroy } from "svelte";
  import {
    IdentifyStore,
    setIdentifyStore,
  } from "$lib/components/identify/identify-store.svelte";

  let { children } = $props();

  const store = new IdentifyStore();
  setIdentifyStore(store);

  onDestroy(() => {
    store.destroy();
  });
</script>

{@render children()}
