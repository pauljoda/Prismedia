<script lang="ts">
  import type { Snippet } from "svelte";
  import { Button, Sheet } from "@prismedia/ui-svelte";
  import { X } from "@lucide/svelte";

  let closeButton = $state<HTMLButtonElement | null>(null);
  let { open, onClose, children }: { open: boolean; onClose: () => void; children: Snippet } = $props();
</script>

<Sheet.Root {open} onOpenChange={next => { if (!next) onClose(); }}>
  {#if open}
    <Sheet.Content onOpenAutoFocus={event => { event.preventDefault(); closeButton?.focus(); }} side="left" showCloseButton={false} class="w-[min(22rem,86vw)] gap-0 sm:max-w-[22rem]">
      <Sheet.Header class="flex-row items-center justify-between border-b px-4 pt-[max(0.75rem,env(safe-area-inset-top))] pb-3">
        <Sheet.Title>Contents</Sheet.Title>
        <Button variant="ghost" size="icon" bind:ref={closeButton} aria-label="Close contents" onclick={onClose}><X /></Button>
      </Sheet.Header>
      <nav aria-label="Table of contents" class="min-h-0 flex-1 overflow-y-auto p-2 pb-[max(1rem,env(safe-area-inset-bottom))]">
        {@render children()}
      </nav>
    </Sheet.Content>
  {/if}
</Sheet.Root>
