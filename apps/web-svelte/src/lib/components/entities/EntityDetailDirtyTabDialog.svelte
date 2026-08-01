<script lang="ts">
  import { Button, Dialog } from "@prismedia/ui-svelte";

  interface Props {
    open: boolean;
    onDiscard: () => void;
    onStay: () => void;
  }

  let { open, onDiscard, onStay }: Props = $props();
</script>

<div class="edit-confirm">
  <Dialog {open} onClose={onStay} ariaLabel="Discard unsaved edits?" dismissible={false} class="edit-confirm-dialog">
    <div class="edit-confirm-content">
      <h2>Discard unsaved edits?</h2>
      <p>Changing tabs will leave the current edit session.</p>
      <div class="edit-confirm-actions">
        <Button type="button" variant="ghost" size="sm" onclick={onStay}>Stay here</Button>
        <Button type="button" variant="danger" size="sm" onclick={onDiscard}>Discard changes</Button>
      </div>
    </div>
  </Dialog>
</div>

<style>
  .edit-confirm :global(.edit-confirm-dialog) {
    width: min(100%, 24rem);
    color: var(--detail-text-secondary, #c4c9d4);
  }

  .edit-confirm-content {
    padding: 1rem;
  }

  .edit-confirm h2 {
    margin: 0;
    color: var(--detail-text, #f2eed8);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1rem;
    letter-spacing: 0;
  }

  .edit-confirm p {
    margin: 0.45rem 0 1rem;
    color: var(--detail-text-muted, #8a93a6);
    font-size: 0.82rem;
  }

  .edit-confirm-actions {
    display: flex;
    align-items: center;
    justify-content: end;
    gap: 0.5rem;
    flex-wrap: wrap;
  }

</style>
