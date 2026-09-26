<script lang="ts">
  import { goto } from "$app/navigation";
  import { Alert, Badge, Button, Panel } from "@prismedia/ui-svelte";
  import { ArrowUpRight, Download, RotateCcw, X } from "@lucide/svelte";
  import { INTEGRATION_TRANSFER_MODE } from "$lib/api/generated/codes";
  import type { IntegrationTransferResponse } from "$lib/api/generated/model";
  import { cancelPublicationTransfer, retryPublicationTransfer } from "$lib/api/integration-transfers";
  import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";
  import { isTransferTerminal, transferCancelLabel, transferStatusLabel, transferRetryLabel, transferProblem } from "$lib/integrations/transfer-labels";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { formatRelativeTime } from "$lib/utils/format";
  import SourceAttribution from "./SourceAttribution.svelte";

  let { transfers, onrefresh, title = null, description = null }: {
    transfers: IntegrationTransferResponse[];
    onrefresh: () => Promise<void>;
    title?: string | null;
    description?: string | null;
  } = $props();
  const nsfw = useNsfw();
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
      const href = await resolveEntityHrefById(id, { hideNsfw: nsfw.mode !== "show" });
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
    {#if title}<div class="flex flex-wrap items-end justify-between gap-3"><div><h2 class="text-base font-semibold">{title}</h2>{#if description}<p class="mt-1 text-sm text-text-muted">{description}</p>{/if}</div><span class="font-mono text-xs text-text-muted">{transfers.length}</span></div>{/if}
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#each transfers as transfer (transfer.id)}
      <Panel class="transfer-row min-w-0 p-4">
        <div class="transfer-icon" aria-hidden="true"><Download /></div>
        <div class="min-w-0 space-y-2">
          <div class="flex flex-wrap items-center gap-2"><h3 class="break-words text-sm font-medium">{transfer.title}</h3><Badge>{transferStatusLabel(transfer)}</Badge></div>
          <div class="flex flex-wrap gap-x-3 gap-y-1 text-xs text-text-muted"><span>Updated {formatRelativeTime(transfer.updatedAt)}</span>{#if Number(transfer.artifactCount) > 0}<span>{transfer.artifactCount} {Number(transfer.artifactCount) === 1 ? "file" : "files"}</span>{/if}</div>
          {#if transfer.mode === INTEGRATION_TRANSFER_MODE.sourceRequest && transfer.canCancel}<p class="text-xs text-text-muted">Stopping this import leaves the source’s download running.</p>{/if}
          {#if transferProblem(transfer)}<p class="break-words text-sm text-text-muted">{transferProblem(transfer)}</p>{/if}
          {#if transfer.sourcePublication?.attribution}<SourceAttribution attribution={transfer.sourcePublication.attribution} compact />{/if}
        </div>
        <div class="flex shrink-0 flex-wrap items-start gap-2">
          {#if transfer.canCancel}
            <Button variant="ghost" size="sm" disabled={busy !== null} onclick={() => void cancel(transfer.id)}><X />{transferCancelLabel(transfer.mode)}</Button>
          {/if}
          {#if transferProblem(transfer) && !isTransferTerminal(transfer.phase)}
            <Button variant="secondary" size="sm" disabled={busy !== null} onclick={() => void retry(transfer.id)}><RotateCcw />{transferRetryLabel(transfer)}</Button>
          {/if}
          {#each transfer.importedEntityIds as id}
            <Button variant="secondary" size="sm" disabled={busy !== null} onclick={() => void open(id)}>Open in library<ArrowUpRight /></Button>
          {/each}
        </div>
      </Panel>
    {/each}
  </section>
{/if}

<style>
  :global(.transfer-row) { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 0.85rem; }
  .transfer-icon { display: grid; width: 2rem; height: 2rem; place-items: center; border-radius: var(--radius-xs); background: var(--color-surface-3); color: var(--color-text-muted); }
  .transfer-icon :global(svg) { width: 1rem; height: 1rem; }
  :global(.transfer-row) > :last-child { grid-column: 2; }
  @media (min-width: 640px) { :global(.transfer-row) { grid-template-columns: auto minmax(0, 1fr) auto; } :global(.transfer-row) > :last-child { grid-column: auto; } }
</style>
