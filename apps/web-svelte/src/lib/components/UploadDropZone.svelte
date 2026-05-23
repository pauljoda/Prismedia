<script lang="ts">
  import type { Snippet } from "svelte";
  import { AlertCircle, Check, Loader2, UploadCloud } from "@lucide/svelte";
  import DestinationPicker from "$lib/components/DestinationPicker.svelte";
  import LibraryRootPicker from "$lib/components/LibraryRootPicker.svelte";
  import { createUploader } from "$lib/upload/uploader.svelte";
  import {
    acceptForCategory,
    categoryForTarget,
    dragHasFiles,
    uploadTargetLabel,
    type UploadTarget,
  } from "$lib/upload/upload-types";

  interface Props {
    target: UploadTarget;
    onUploaded?: () => void | Promise<void>;
    enabled?: boolean;
    dropLabel?: string;
    class?: string;
    children?: Snippet;
  }

  let {
    target,
    onUploaded,
    enabled = true,
    dropLabel,
    class: className = "relative",
    children,
  }: Props = $props();

  // svelte-ignore state_referenced_locally
  const uploader = createUploader({ target, onUploaded });
  const category = $derived(categoryForTarget(target));
  const accept = $derived(acceptForCategory(category));
  let isDragging = $state(false);
  let dragDepth = 0;

  const total = $derived(uploader.files.length);
  const done = $derived(uploader.files.filter((f) => f.status === "done").length);
  const failed = $derived(uploader.files.filter((f) => f.status === "error").length);
  const errors = $derived(uploader.files.filter((f) => f.status === "error"));

  function onDragEnter(event: DragEvent) {
    if (!enabled || !dragHasFiles(event.dataTransfer)) return;
    event.preventDefault();
    event.stopPropagation();
    dragDepth += 1;
    isDragging = true;
  }

  function onDragOver(event: DragEvent) {
    if (!enabled || !dragHasFiles(event.dataTransfer)) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = "copy";
  }

  function onDragLeave() {
    if (!enabled) return;
    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) isDragging = false;
  }

  function onDrop(event: DragEvent) {
    if (!enabled) return;
    event.preventDefault();
    event.stopPropagation();
    dragDepth = 0;
    isDragging = false;
    const files = event.dataTransfer?.files;
    if (files?.length) void uploader.uploadFiles(files);
  }
</script>

<svelte:window
  ondragover={(event) => {
    if (enabled && dragHasFiles(event.dataTransfer)) event.preventDefault();
  }}
  ondrop={(event) => {
    if (enabled && dragHasFiles(event.dataTransfer)) event.preventDefault();
    dragDepth = 0;
    isDragging = false;
  }}
  ondragend={() => {
    dragDepth = 0;
    isDragging = false;
  }}
/>

<div
  class={className}
  role="region"
  aria-label="File upload drop zone"
  ondragenter={onDragEnter}
  ondragover={onDragOver}
  ondragleave={onDragLeave}
  ondrop={onDrop}
>
  {#if children}{@render children()}{/if}

  {#if isDragging && enabled}
    <div class="pointer-events-none fixed inset-0 z-[80] flex items-center justify-center bg-bg/80 backdrop-blur-sm">
      <div class="flex flex-col items-center gap-3 border border-border-accent bg-surface-1/95 px-10 py-8 shadow-[var(--shadow-glow-accent-strong)]">
        <UploadCloud class="h-10 w-10 text-text-accent" />
        <div class="text-sm font-medium text-text-primary">
          {dropLabel ?? `Drop ${uploadTargetLabel(target)} to import`}
        </div>
        <div class="font-mono text-[0.7rem] text-text-muted">{accept}</div>
      </div>
    </div>
  {/if}

  {#if total > 0}
    <div class="fixed bottom-4 right-4 z-40 w-[min(340px,calc(100vw-2rem))] border border-border-subtle bg-surface-2/95 shadow-lg backdrop-blur-md">
      <div class="flex items-center justify-between gap-3 border-b border-border-subtle px-3 py-2">
        <div class="flex min-w-0 items-center gap-2 text-[0.78rem] font-medium text-text-primary">
          {#if uploader.isUploading}
            <Loader2 class="h-3.5 w-3.5 flex-shrink-0 animate-spin text-text-accent" />
          {:else if failed > 0}
            <AlertCircle class="h-3.5 w-3.5 flex-shrink-0 text-error-text" />
          {:else}
            <Check class="h-3.5 w-3.5 flex-shrink-0 text-success-text" />
          {/if}
          <span class="truncate">
            {uploader.isUploading
              ? `Uploading ${Math.min(done + 1, total)} of ${total}`
              : failed > 0
                ? `Uploaded ${done} of ${total} (${failed} failed)`
                : `Uploaded ${done} of ${total}`}
          </span>
        </div>
        <button
          type="button"
          onclick={uploader.resetState}
          disabled={uploader.isUploading}
          class="text-[0.7rem] text-text-muted hover:text-text-primary disabled:opacity-50"
        >
          Dismiss
        </button>
      </div>
      {#if errors.length > 0}
        <div class="max-h-40 space-y-1 overflow-y-auto px-3 py-2">
          {#each errors as entry, i (`${entry.file.name}-${i}`)}
            <div class="text-[0.68rem] text-error-text">
              <span class="font-mono">{entry.file.name}</span>: {entry.error}
            </div>
          {/each}
        </div>
      {/if}
    </div>
  {/if}

  <LibraryRootPicker
    open={uploader.needsRootPicker}
    roots={uploader.candidateRoots}
    onConfirm={uploader.confirmRootPick}
    onCancel={uploader.cancelRootPick}
  />
  <DestinationPicker
    open={uploader.needsAudioLibraryPicker}
    title="Choose an audio library"
    description="Pick the library these tracks should land in."
    items={uploader.candidateAudioLibraries.map((library) => ({
      id: library.id,
      title: library.title,
      subtitle: `${library.trackCount} track${library.trackCount === 1 ? "" : "s"}`,
    }))}
    onConfirm={uploader.confirmAudioLibraryPick}
    onCancel={uploader.cancelAudioLibraryPick}
  />
</div>
