<script lang="ts">
  import { RotateCcw } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import { clearBlocklist } from "$lib/api/acquisitions";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";

  let { entityId, entityTitle }: { entityId: string; entityTitle: string } = $props();

  let confirmOpen = $state(false);
  let busy = $state(false);
  let message = $state<string | null>(null);
  let error = $state<string | null>(null);

  async function clearEntityBlocklist() {
    busy = true;
    message = null;
    error = null;
    try {
      const removed = await clearBlocklist({ entityId });
      message = removed === 1
        ? "Allowed one blocked release again."
        : `Allowed ${removed} blocked releases again.`;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to clear this item's blocklist";
      throw err;
    } finally {
      busy = false;
    }
  }
</script>

<div class="flex flex-col items-start gap-control-gap">
  <Button
    type="button"
    variant="ghost"
    class="h-auto min-h-control justify-start whitespace-normal text-left"
    disabled={busy}
    onclick={() => (confirmOpen = true)}
  >
    <RotateCcw data-icon="inline-start" />
    Allow blocked releases again
  </Button>
  {#if message}<p role="status" class="text-caption text-muted-foreground">{message}</p>{/if}
  {#if error}<p role="alert" class="text-caption text-destructive">{error}</p>{/if}
</div>

<ConfirmDialog
  open={confirmOpen}
  title={`Allow blocked releases for ${entityTitle}?`}
  message="Every release previously blocked for this item can be selected and downloaded again. Other items stay unchanged."
  confirmLabel="Allow again"
  danger
  onConfirm={clearEntityBlocklist}
  onClose={() => (confirmOpen = false)}
/>
