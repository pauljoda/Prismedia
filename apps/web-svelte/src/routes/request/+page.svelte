<script lang="ts">
  import { Send, Settings } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import RequestDiscover from "$lib/components/requests/RequestDiscover.svelte";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();
</script>

<svelte:head><title>Request · Prismedia</title></svelte:head>

<div class="space-y-5">
  <div class="flex flex-wrap items-start justify-between gap-3">
    <div>
      <h1 class="flex items-center gap-2.5">
        <Send class="h-5 w-5 text-text-accent" />
        Request
      </h1>
      <p class="mt-1 text-[0.78rem] text-text-muted">
        Choose a content kind and metadata source, then review exactly what Prismedia will request
      </p>
    </div>
    {#if session.isAdmin}
      <Button
        type="button"
        variant="secondary"
        size="sm"
        onclick={() => void goto("/settings/acquisition")}
        class="no-lift gap-1.5 px-3 py-1.5 text-xs"
      >
        <Settings class="h-3.5 w-3.5" />
        Settings
      </Button>
    {/if}
  </div>

  <RequestDiscover />
</div>
