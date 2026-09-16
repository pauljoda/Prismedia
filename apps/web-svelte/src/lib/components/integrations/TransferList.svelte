<script lang="ts">
  import { goto } from "$app/navigation";
  import { Alert, Badge, Button, Panel } from "@prismedia/ui-svelte";
  import { ArrowUpRight, RotateCcw } from "@lucide/svelte";
  import type { IntegrationTransferResponse } from "$lib/api/generated/model";
  import { retryPublicationTransfer } from "$lib/api/integration-transfers";
  import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";
  import { isTransferTerminal, transferPhaseLabels } from "$lib/integrations/transfer-labels";

  let { transfers, onrefresh }: { transfers: IntegrationTransferResponse[]; onrefresh: () => Promise<void> } = $props();
  let busy = $state<string | null>(null);
  let error = $state<string | null>(null);

  async function retry(id: string) {
    busy = id; error = null;
    try { await retryPublicationTransfer(id); await onrefresh(); }
    catch (cause) { error = cause instanceof Error ? cause.message : "Could not retry this import"; }
    finally { busy = null; }
  }
  async function open(id: string) {
    busy = id; error = null;
    try {
      const href = await resolveEntityHrefById(id);
      if (!href) throw new Error("The imported entity does not have a library page yet.");
      await goto(href);
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not open the imported publication"; }
    finally { busy = null; }
  }
</script>

{#if transfers.length}
  <section class="flex min-w-0 flex-col gap-3" aria-label="Publication imports">
    <h2 class="text-base font-semibold">Recent imports</h2>
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#each transfers as transfer (transfer.id)}
      <Panel class="flex min-w-0 flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
        <div class="min-w-0 space-y-2">
          <h3 class="break-words text-sm font-medium">{transfer.title}</h3>
          <Badge>{transferPhaseLabels[transfer.phase]}</Badge>
          {#if transfer.lastError}<p class="break-words text-sm text-text-muted">{transfer.lastError}</p>{/if}
        </div>
        <div class="flex shrink-0 flex-wrap gap-2">
          {#if transfer.lastError && !isTransferTerminal(transfer.phase)}
            <Button variant="secondary" size="sm" disabled={busy !== null} onclick={() => void retry(transfer.id)}><RotateCcw />Retry import</Button>
          {/if}
          {#each transfer.importedEntityIds as id}
            <Button variant="secondary" size="sm" disabled={busy !== null} onclick={() => void open(id)}>Open in library<ArrowUpRight /></Button>
          {/each}
        </div>
      </Panel>
    {/each}
  </section>
{/if}
