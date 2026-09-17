<script lang="ts">
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import RequestDiscover from "./RequestDiscover.svelte";

  import type { ConnectionResponse } from "$lib/api/generated/model";
  let { back = null, connections = [], initialConnectionId = null }: { back?: string | null; connections?: ConnectionResponse[]; initialConnectionId?: string | null } = $props();

  const nsfw = provideNsfw(() => ({ initialMode: "off", allowed: true }));
</script>

<button type="button" onclick={() => nsfw.toggleShowOff()}>
  {nsfw.mode === "show" ? "Hide NSFW" : "Show NSFW"}
</button>
<RequestDiscover {back} {connections} {initialConnectionId} onConnectionChange={id => initialConnectionId = id} />
