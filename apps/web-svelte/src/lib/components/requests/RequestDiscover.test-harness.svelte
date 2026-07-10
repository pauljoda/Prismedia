<script lang="ts">
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import RequestDiscover from "./RequestDiscover.svelte";

  let { back = null }: { back?: string | null } = $props();

  const nsfw = provideNsfw(() => ({ initialMode: "off", allowed: true }));
</script>

<button type="button" onclick={() => nsfw.toggleShowOff()}>
  {nsfw.mode === "show" ? "Hide NSFW" : "Show NSFW"}
</button>
<RequestDiscover {back} />
