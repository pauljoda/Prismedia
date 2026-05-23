<script lang="ts">
  import type { ComponentProps } from "svelte";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import EntityGrid from "./EntityGrid.svelte";

  const props: ComponentProps<typeof EntityGrid> = $props();

  provideNsfw(() => ({ initialMode: "show", lanAutoEnable: false }));
</script>

<EntityGrid {...props} />
