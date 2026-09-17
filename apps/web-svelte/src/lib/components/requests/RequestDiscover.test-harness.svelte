<script lang="ts">
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import RequestDiscover from "./RequestDiscover.svelte";

  import type { ConnectionResponse } from "$lib/api/generated/model";
  import type { RequestMediaKindCode } from "$lib/api/generated/codes";
  let { back = null, connections = [], initialConnectionId = null, initialKind }: { back?: string | null; connections?: ConnectionResponse[]; initialConnectionId?: string | null; initialKind?: RequestMediaKindCode | null } = $props();

  const nsfw = provideNsfw(() => ({ initialMode: "off", allowed: true }));
</script>

<button type="button" onclick={() => nsfw.toggleShowOff()}>
  {nsfw.mode === "show" ? "Hide NSFW" : "Show NSFW"}
</button>
<RequestDiscover {back} {connections} {initialConnectionId} {initialKind} onConnectionChange={id => initialConnectionId = id} onKindChange={kind => initialKind = kind} />
