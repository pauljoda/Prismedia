<script lang="ts">
  import type { ComponentProps } from "svelte";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import EntityDetail from "./EntityDetail.svelte";

  const props: ComponentProps<typeof EntityDetail> = $props();

  provideNsfw(() => ({ initialMode: "show", lanAutoEnable: false }));
</script>

<EntityDetail {...props} />
