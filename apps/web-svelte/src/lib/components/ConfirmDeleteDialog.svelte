<script module lang="ts">
  export type DeletableEntity =
    | "video"
    | "image"
    | "audio-track"
    | "gallery"
    | "book"
    | "audio-library"
    | "series";
</script>

<script lang="ts">
  import { AlertTriangle, Loader2 } from "@lucide/svelte";
  import { dur, ease, flyUp } from "@prismedia/ui-svelte";
  import { fade } from "svelte/transition";

  interface Props {
    open: boolean;
    entityType: DeletableEntity;
    count: number;
    onClose: () => void;
    onDeleteFromLibrary: () => void | Promise<void>;
    onDeleteFromDisk?: () => void | Promise<void>;
    allowDeleteFromDisk?: boolean;
    loading?: boolean;
  }

  let {
    open,
    entityType,
    count,
    onClose,
    onDeleteFromLibrary,
    onDeleteFromDisk,
    allowDeleteFromDisk = false,
    loading = false,
  }: Props = $props();

  const labels: Record<DeletableEntity, { singular: string; plural: string }> = {
    video: { singular: "video", plural: "videos" },
    image: { singular: "image", plural: "images" },
    "audio-track": { singular: "track", plural: "tracks" },
    gallery: { singular: "gallery", plural: "galleries" },
    book: { singular: "book", plural: "books" },
    "audio-library": { singular: "audio library", plural: "audio libraries" },
    series: { singular: "series", plural: "series" },
  };

  const noun = $derived(count === 1 ? labels[entityType].singular : labels[entityType].plural);
  const showDiskOption = $derived(allowDeleteFromDisk && typeof onDeleteFromDisk === "function");
</script>

{#if open}
  <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
    <button
      type="button"
      class="absolute inset-0 bg-black/80 backdrop-blur-sm"
      onclick={() => {
        if (!loading) onClose();
      }}
      aria-label="Close delete dialog"
      transition:fade={{ duration: dur.normal, easing: ease.enter }}
    ></button>

    <div
      role="dialog"
      aria-modal="true"
      aria-label={`Delete ${noun}`}
      class="relative z-10 w-full max-w-md surface-elevated p-6"
      transition:flyUp
    >
      <div class="flex items-start gap-3">
        <div class="flex h-10 w-10 flex-shrink-0 items-center justify-center border border-error/30 bg-error-muted/40">
          <AlertTriangle class="h-5 w-5 text-error-text" />
        </div>
        <div class="min-w-0">
          <h2 class="text-base font-heading font-semibold text-text-primary">
            Delete {count} {noun}
          </h2>
          <p class="mt-1.5 text-[0.78rem] leading-relaxed text-text-muted">
            {#if showDiskOption}
              Remove the selected {noun} from Prismedia, or remove the library entries and source
              file{count === 1 ? "" : "s"} from disk.
            {:else}
              Remove the selected {noun} from Prismedia. Generated files and associations will also
              be cleaned up.
            {/if}
          </p>
          <p class="mt-1 text-[0.72rem] font-medium text-error-text">
            This action cannot be undone.
          </p>
        </div>
      </div>

      <div class="mt-5 flex flex-col gap-2">
        <button
          type="button"
          onclick={() => void onDeleteFromLibrary()}
          disabled={loading}
          aria-label="Remove from Library"
          class="inline-flex w-full items-center justify-center gap-2 bg-error-muted/60 px-3 py-2 text-sm font-medium text-error-text transition-colors hover:bg-error-muted disabled:opacity-50"
        >
          {#if loading}<Loader2 class="h-4 w-4 animate-spin" />{/if}
          Delete from Library
        </button>
        {#if showDiskOption && onDeleteFromDisk}
          <button
            type="button"
            onclick={() => void onDeleteFromDisk()}
            disabled={loading}
            class="inline-flex w-full items-center justify-center gap-2 border border-error/40 px-3 py-2 text-sm font-medium text-error-text transition-colors hover:bg-error-muted/20 disabled:opacity-50"
          >
            Delete from Library and Disk
          </button>
        {/if}
        <button
          type="button"
          onclick={onClose}
          disabled={loading}
          class="inline-flex w-full items-center justify-center gap-2 px-3 py-2 text-sm font-medium text-text-muted transition-colors hover:bg-surface-2 hover:text-text-primary disabled:opacity-50"
        >
          Cancel
        </button>
      </div>
    </div>
  </div>
{/if}
