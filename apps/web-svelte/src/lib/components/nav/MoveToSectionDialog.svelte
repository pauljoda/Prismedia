<script lang="ts">
  import { Check, X } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface SectionOption {
    id: string;
    label: string;
  }

  interface Props {
    open: boolean;
    /** Label of the item being moved, for the header. */
    itemLabel: string;
    sections: SectionOption[];
    currentSectionId: string;
    onMove: (sectionId: string) => void;
    onClose: () => void;
  }

  let { open, itemLabel, sections, currentSectionId, onMove, onClose }: Props = $props();

  let dialogRef = $state<HTMLDialogElement | null>(null);

  $effect(() => {
    if (!dialogRef) return;
    if (open) dialogRef.showModal();
    else if (dialogRef.open) dialogRef.close();
  });

  function choose(id: string) {
    if (id !== currentSectionId) onMove(id);
    onClose();
  }

  function handleBackdropClick(event: MouseEvent) {
    if (event.target === dialogRef) onClose();
  }
</script>

<dialog
  bind:this={dialogRef}
  onclick={handleBackdropClick}
  onclose={onClose}
  aria-label="Move to section"
  class="app-dialog-surface fixed inset-0 m-auto h-fit w-[min(92vw,24rem)] p-0 text-text-primary open:block"
>
  <div class="flex flex-col gap-3 p-4">
    <div class="flex items-start justify-between gap-4">
      <div class="min-w-0">
        <p class="text-kicker text-text-accent">Move to section</p>
        <h2 class="truncate font-heading text-base font-semibold tracking-wide text-text-primary">
          {itemLabel}
        </h2>
      </div>
      <button
        type="button"
        onclick={onClose}
        class="flex h-8 w-8 shrink-0 items-center justify-center rounded-sm text-text-muted transition-colors hover:bg-surface-2 hover:text-text-primary"
        aria-label="Cancel"
      >
        <X class="h-4 w-4" />
      </button>
    </div>

    <ul class="flex flex-col gap-1">
      {#each sections as section (section.id)}
        {@const current = section.id === currentSectionId}
        <li>
          <button
            type="button"
            onclick={() => choose(section.id)}
            aria-current={current ? "true" : undefined}
            class={cn(
              "flex w-full items-center justify-between gap-3 rounded-sm border px-3 py-2.5 text-left text-sm transition-colors",
              current
                ? "border-border-accent bg-accent-950 text-text-accent"
                : "border-transparent text-text-secondary hover:border-border-default hover:bg-surface-2 hover:text-text-primary",
            )}
          >
            <span class="truncate">{section.label}</span>
            {#if current}
              <Check class="h-4 w-4 shrink-0" />
            {/if}
          </button>
        </li>
      {/each}
    </ul>
  </div>
</dialog>
