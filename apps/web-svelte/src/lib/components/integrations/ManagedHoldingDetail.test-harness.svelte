<script lang="ts">
  import type { ComponentProps } from "svelte";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import { provideSession } from "$lib/stores/session.svelte";
  import ManagedHoldingDetail from "./ManagedHoldingDetail.svelte";

  const props: ComponentProps<typeof ManagedHoldingDetail> = $props();

  provideNsfw(() => ({ initialMode: "show", allowed: true }));
  provideSession({ user: null, needsSetup: false });
</script>

<ManagedHoldingDetail {...props} />
