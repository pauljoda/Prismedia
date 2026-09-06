<script lang="ts">
  import { Button, DialogBase } from "@prismedia/ui-svelte";

  interface Props {
    open: boolean;
    onDiscard: () => void;
    onStay: () => void;
  }

  let { open, onDiscard, onStay }: Props = $props();
</script>

<DialogBase.Root {open} onOpenChange={(next) => { if (!next) onStay(); }}>
  <DialogBase.Content
    showCloseButton={false}
    escapeKeydownBehavior="ignore"
    interactOutsideBehavior="ignore"
  >
    <DialogBase.Header>
      <DialogBase.Title>Discard unsaved edits?</DialogBase.Title>
      <DialogBase.Description>Changing tabs will leave the current edit session.</DialogBase.Description>
    </DialogBase.Header>
    <DialogBase.Footer class="flex-col">
      <Button type="button" variant="outline" onclick={onStay}>Stay here</Button>
      <Button type="button" variant="danger" onclick={onDiscard}>Discard changes</Button>
    </DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>
