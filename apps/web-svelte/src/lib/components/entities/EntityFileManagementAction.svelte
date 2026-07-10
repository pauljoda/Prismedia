<script lang="ts">
  import { Trash2 } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import type { EntityCapability } from "$lib/api/generated/model";
  import { canDeleteEntityFiles } from "$lib/api/capabilities";
  import { deleteMediaEntity } from "$lib/api/entity-deletion";
  import ConfirmDialog from "./ConfirmDialog.svelte";
  import EntityActionButton from "./EntityActionButton.svelte";

  let {
    entity,
    onDeleted,
    onReverted,
    compact = false,
  }: {
    entity: { id: string; title: string; capabilities: EntityCapability[] };
    onDeleted: () => void | Promise<void>;
    onReverted: () => void | Promise<void>;
    compact?: boolean;
  } = $props();

  let confirmOpen = $state(false);
  const canDelete = $derived(canDeleteEntityFiles(entity.capabilities));

  async function handleConfirmDelete() {
    const result = await deleteMediaEntity(entity.id, true);
    if ((Number(result.reverted) || 0) > 0) {
      await onReverted();
      return;
    }

    await onDeleted();
  }
</script>

{#if canDelete}
  {#if compact}
    <Button
      type="button"
      variant="danger"
      size="sm"
      onclick={() => { confirmOpen = true; }}
      class="no-lift ml-auto gap-1.5 px-2.5 py-1 text-xs"
      title="Permanently delete this Entity's managed files and reconcile its acquisition state"
    >
      <Trash2 class="h-3.5 w-3.5" />
      Delete files
    </Button>
  {:else}
    <EntityActionButton
      label="Delete files"
      icon={Trash2}
      variant="danger"
      ariaLabel={`Delete files for ${entity.title}`}
      title="Permanently delete this Entity's managed files and reconcile its acquisition state"
      onClick={() => { confirmOpen = true; }}
    />
  {/if}

  <ConfirmDialog
    open={confirmOpen}
    title={`Delete the files for "${entity.title}"?`}
    message="This permanently deletes the Entity's managed source files and every structural child beneath it. Monitored content goes back to Wanted and is searched again; unmonitored content is removed from the library. This cannot be undone."
    confirmLabel="Delete files"
    danger
    onConfirm={handleConfirmDelete}
    onClose={() => (confirmOpen = false)}
  />
{/if}
