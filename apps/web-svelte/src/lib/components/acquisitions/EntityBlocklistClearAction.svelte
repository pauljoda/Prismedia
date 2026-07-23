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

<div class="flex flex-wrap items-center gap-2">
  <Button
    type="button"
    variant="ghost"
    size="sm"
    class="no-lift gap-1.5 px-2.5 py-1 text-xs"
    disabled={busy}
    onclick={() => (confirmOpen = true)}
  >
    <RotateCcw class="h-3.5 w-3.5" />
    Allow blocked releases again
  </Button>
  {#if message}<p role="status" class="text-[0.72rem] text-text-muted">{message}</p>{/if}
  {#if error}<p role="alert" class="text-[0.72rem] text-error-text">{error}</p>{/if}
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
