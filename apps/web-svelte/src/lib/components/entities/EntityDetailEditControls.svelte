<script lang="ts">
  import { Save, X } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";

  interface Props {
    cancelLabel: string;
    errors: readonly string[];
    onCancel: () => void;
    onSave: () => void;
    saveDisabled: boolean;
    saveLabel: string;
    saving: boolean;
  }

  let {
    cancelLabel,
    errors,
    onCancel,
    onSave,
    saveDisabled,
    saveLabel,
    saving,
  }: Props = $props();
</script>

<div class="detail-edit-toolbar">
  <div class="detail-edit-actions">
    <Button
      type="button"
      variant="ghost"
      size="sm"
      class="font-mono text-[0.68rem] font-bold uppercase tracking-[0.04em]"
      onclick={onCancel}
      disabled={saving}
      aria-label={cancelLabel}
    >
      <X class="h-3.5 w-3.5" />
      Cancel
    </Button>
    <Button
      type="button"
      variant="secondary"
      size="sm"
      class="font-mono text-[0.68rem] font-bold uppercase tracking-[0.04em] shadow-[inset_2px_0_0_color-mix(in_srgb,var(--detail-accent)_72%,#c7c9cc)]"
      onclick={onSave}
      disabled={saveDisabled}
      aria-label={saveLabel}
    >
      <Save class="h-3.5 w-3.5" />
      {saving ? "Saving…" : "Save"}
    </Button>
  </div>
</div>

{#if errors.length > 0}
  <div class="edit-errors" aria-live="polite">
    {#each errors as error (error)}
      <p>{error}</p>
    {/each}
  </div>
{/if}

<style>
  .detail-edit-toolbar {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 0.5rem;
    padding: 0.5rem 1.5rem;
    border-bottom: 1px solid var(--detail-border);
    background: var(--detail-glass);
    backdrop-filter: blur(var(--detail-glass-blur));
    -webkit-backdrop-filter: blur(var(--detail-glass-blur));
  }

  .detail-edit-actions {
    display: flex;
    align-items: center;
    justify-content: end;
    gap: 0.5rem;
    flex-wrap: wrap;
  }

  .edit-errors {
    display: grid;
    gap: 0.25rem;
    padding: 0.65rem 1.5rem;
    border-bottom: 1px solid color-mix(in srgb, #ef4444 45%, var(--detail-border));
    border-radius: var(--radius-xs, 4px);
    background: color-mix(in srgb, #ef4444 8%, var(--detail-surface));
    color: #fca5a5;
    font-size: 0.78rem;
  }

  .edit-errors p {
    margin: 0;
  }
</style>
