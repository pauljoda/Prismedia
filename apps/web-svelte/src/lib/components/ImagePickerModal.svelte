<script lang="ts">
  import { onMount } from "svelte";
  import { X, Check, ChevronLeft, ChevronRight } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    images: string[];
    selectedIndex: number;
    onSelect: (index: number) => void;
    onClose: () => void;
    title?: string;
  }

  let {
    images,
    selectedIndex,
    onSelect,
    onClose,
    title = "Select Image",
  }: Props = $props();

  onMount(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
      if (e.key === "ArrowLeft")
        onSelect(selectedIndex > 0 ? selectedIndex - 1 : images.length - 1);
      if (e.key === "ArrowRight")
        onSelect(selectedIndex < images.length - 1 ? selectedIndex + 1 : 0);
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });
</script>

{#if images.length > 0}
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    class="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-sm"
    onclick={onClose}
  >
    <!-- svelte-ignore a11y_click_events_have_key_events -->
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      class="relative max-w-5xl w-full max-h-[90vh] mx-4 flex flex-col"
      onclick={(e) => e.stopPropagation()}
    >
      <div class="flex items-center justify-between px-4 py-3 surface-elevated">
        <span class="text-sm font-medium text-text-primary">
          {title} ({selectedIndex + 1} of {images.length})
        </span>
        <button
          type="button"
          onclick={onClose}
          class="text-text-muted hover:text-text-primary transition-colors"
          aria-label="Close"
        >
          <X class="h-5 w-5" />
        </button>
      </div>

      <div class="flex-1 min-h-0 surface-panel flex items-center justify-center p-4 overflow-hidden relative">
        {#if images.length > 1}
          <button
            type="button"
            onclick={() =>
              onSelect(selectedIndex > 0 ? selectedIndex - 1 : images.length - 1)}
            class="absolute left-2 z-10 p-2 bg-black/50 text-white hover:bg-black/70 transition-colors"
            aria-label="Previous"
          >
            <ChevronLeft class="h-5 w-5" />
          </button>
          <button
            type="button"
            onclick={() =>
              onSelect(selectedIndex < images.length - 1 ? selectedIndex + 1 : 0)}
            class="absolute right-2 z-10 p-2 bg-black/50 text-white hover:bg-black/70 transition-colors"
            aria-label="Next"
          >
            <ChevronRight class="h-5 w-5" />
          </button>
        {/if}
        <img
          src={images[selectedIndex] ?? images[0]}
          alt={`Image ${selectedIndex + 1}`}
          class="max-w-full max-h-[60vh] object-contain"
        />
      </div>

      <div class="surface-elevated p-4">
        <div class="grid grid-cols-4 sm:grid-cols-6 md:grid-cols-8 gap-2 max-h-48 overflow-y-auto">
          {#each images as url, i (i)}
            <button
              type="button"
              onclick={() => onSelect(i)}
              class={cn(
                "aspect-[3/4] overflow-hidden bg-surface-3 border-2 transition-all duration-fast",
                i === selectedIndex
                  ? "border-border-accent ring-2 ring-accent-500/30"
                  : "border-transparent hover:border-border-subtle opacity-60 hover:opacity-100",
              )}
            >
              <img
                src={url}
                alt={`Option ${i + 1}`}
                class="w-full h-full object-cover"
                loading="lazy"
              />
            </button>
          {/each}
        </div>
        <div class="flex justify-end mt-3 gap-2">
          <button
            type="button"
            onclick={onClose}
            class={cn(
              "flex items-center gap-1.5 px-4 py-2 text-xs font-medium transition-all duration-fast",
              "bg-accent-950 text-text-accent border border-border-accent hover:bg-accent-900",
            )}
          >
            <Check class="h-3 w-3" />
            Use Selected
          </button>
        </div>
      </div>
    </div>
  </div>
{/if}
