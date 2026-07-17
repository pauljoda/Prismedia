<script lang="ts">
  import type { Snippet } from "svelte";
  import { cn } from "../lib/utils";

  interface Props {
    open: boolean;
    ariaLabel: string;
    onClose: () => void;
    dismissible?: boolean;
    class?: string;
    children: Snippet;
  }

  let {
    open,
    ariaLabel,
    onClose,
    dismissible = true,
    class: className,
    children,
  }: Props = $props();

  let dialogRef = $state<HTMLDialogElement | null>(null);

  $effect(() => {
    if (!dialogRef) return;
    if (open && !dialogRef.open) dialogRef.showModal();
    else if (!open && dialogRef.open) dialogRef.close();
  });

  function requestClose() {
    if (dismissible) onClose();
  }

  function handleCancel(event: Event) {
    event.preventDefault();
    requestClose();
  }

  function handleClose() {
    if (open) onClose();
  }

  function handleBackdropClick(event: MouseEvent) {
    if (event.target === dialogRef) requestClose();
  }
</script>

<dialog
  bind:this={dialogRef}
  aria-label={ariaLabel}
  oncancel={handleCancel}
  onclose={handleClose}
  onclick={handleBackdropClick}
  class={cn(
    "app-dialog-surface fixed inset-0 m-auto h-fit max-h-[calc(100dvh-2rem)] max-w-[calc(100vw-2rem)] overflow-auto p-0 text-text-primary",
    className,
  )}
>
  {@render children()}
</dialog>
