<script lang="ts">
  import type { ComponentProps } from "svelte";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import EntityDetail from "./EntityDetail.svelte";
  import { provideSession } from "$lib/stores/session.svelte";

  const props: ComponentProps<typeof EntityDetail> = $props();

  provideNsfw(() => ({ initialMode: "show", allowed: true }));
  provideSession({ user: null, needsSetup: false });
</script>

<EntityDetail {...props} />
