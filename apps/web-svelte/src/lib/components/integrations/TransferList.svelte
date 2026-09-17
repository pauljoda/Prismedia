<script lang="ts">
  import { goto } from "$app/navigation";
  import { Alert, Badge, Button, Panel } from "@prismedia/ui-svelte";
  import { ArrowUpRight, RotateCcw, X } from "@lucide/svelte";
  import { INTEGRATION_TRANSFER_MODE } from "$lib/api/generated/codes";
  import type { IntegrationTransferResponse } from "$lib/api/generated/model";
  import { cancelPublicationTransfer, retryPublicationTransfer } from "$lib/api/integration-transfers";
  import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";
  import { isTransferTerminal, transferPhaseLabels } from "$lib/integrations/transfer-labels";
  import SourceAttribution from "./SourceAttribution.svelte";

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
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not open the imported item"; }
    finally { busy = null; }
  }
  async function cancel(id: string) {
    busy = id; error = null;
    try { await cancelPublicationTransfer(id); await onrefresh(); }
    catch (cause) {
      error = cause instanceof Error ? cause.message : "Could not cancel this download";
      await onrefresh();
    }
    finally { busy = null; }
  }
</script>

{#if transfers.length}
  <section class="flex min-w-0 flex-col gap-3" aria-label="Media imports">
    <h2 class="text-base font-semibold">Recent imports</h2>
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#each transfers as transfer (transfer.id)}
      <Panel class="flex min-w-0 flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
        <div class="min-w-0 space-y-2">
          <h3 class="break-words text-sm font-medium">{transfer.title}</h3>
          <Badge>{transfer.cancellationRequested && !isTransferTerminal(transfer.phase) ? "Cancellation requested" : transferPhaseLabels[transfer.phase]}</Badge>
          {#if transfer.lastError}<p class="break-words text-sm text-text-muted">{transfer.lastError}</p>{/if}
          {#if transfer.sourcePublication?.attribution}<SourceAttribution attribution={transfer.sourcePublication.attribution} />{/if}
        </div>
        <div class="flex shrink-0 flex-wrap gap-2">
          {#if transfer.canCancel}
            <Button variant="ghost" size="sm" disabled={busy !== null} onclick={() => void cancel(transfer.id)}><X />{transfer.mode === INTEGRATION_TRANSFER_MODE.remoteExecutor ? "Cancel request" : "Cancel download"}</Button>
          {/if}
          {#if transfer.lastError && !isTransferTerminal(transfer.phase)}
            <Button variant="secondary" size="sm" disabled={busy !== null} onclick={() => void retry(transfer.id)}><RotateCcw />{transfer.cancellationRequested ? "Retry cancellation" : "Retry import"}</Button>
          {/if}
          {#each transfer.importedEntityIds as id}
            <Button variant="secondary" size="sm" disabled={busy !== null} onclick={() => void open(id)}>Open in library<ArrowUpRight /></Button>
          {/each}
        </div>
      </Panel>
    {/each}
  </section>
{/if}
