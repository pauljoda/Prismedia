<script lang="ts">
  import { Button, Dialog } from "@prismedia/ui-svelte";
  import { FolderOpen } from "@lucide/svelte";
  import type { LibraryRootSummaryDto } from "$lib/entities/media-view-models";

  interface Props {
    open: boolean;
    roots: LibraryRootSummaryDto[];
    onConfirm: (rootId: string) => void | Promise<void>;
    onCancel: () => void;
  }

  let { open, roots, onConfirm, onCancel }: Props = $props();
</script>

{#if open}
  <Dialog {open} onClose={onCancel} ariaLabel={"Choose a library root"} class="w-full max-w-lg sm:max-w-lg p-6">

      <div class="space-y-1.5">
        <h2 class="text-base font-heading font-semibold text-text-primary">
          Choose a library
        </h2>
        <p class="text-[0.78rem] leading-relaxed text-text-muted">
          Pick where this upload batch should land.
        </p>
      </div>

      <div class="mt-5 max-h-[50vh] space-y-1.5 overflow-y-auto">
        {#each roots as root (root.id)}
          <Button variant="ghost" size="sm"
            type="button"
            onclick={() => void onConfirm(root.id)}
            class="h-auto group flex w-full items-center gap-3 px-3.5 py-3 text-left"
          >
            <FolderOpen class="h-4 w-4 flex-shrink-0 text-text-muted group-hover:text-text-accent" />
            <span class="min-w-0 flex-1">
              <span class="block truncate text-sm font-medium text-text-primary">{root.label}</span>
              <span class="block truncate font-mono text-[0.7rem] text-text-muted">{root.path}</span>
            </span>
          </Button>
        {/each}
      </div>

      <div class="mt-5 flex justify-end">
        <Button variant="ghost" size="sm"
          type="button"
          onclick={onCancel}
          class="px-3 py-1.5 text-sm text-text-muted hover:bg-surface-2 hover:text-text-primary"
        >
          Cancel
        </Button>
      </div>
  </Dialog>
{/if}
