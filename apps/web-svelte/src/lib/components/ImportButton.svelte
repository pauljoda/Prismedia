<script lang="ts">
  import { Upload } from "@lucide/svelte";
  import DestinationPicker from "$lib/components/DestinationPicker.svelte";
  import LibraryRootPicker from "$lib/components/LibraryRootPicker.svelte";
  import { createUploader } from "$lib/upload/uploader.svelte";
  import {
    acceptForCategory,
    categoryForTarget,
    type UploadTarget,
  } from "$lib/upload/upload-types";

  interface Props {
    target: UploadTarget;
    onUploaded?: () => void | Promise<void>;
    label?: string;
    disabled?: boolean;
  }

  let { target, onUploaded, label = "Import", disabled = false }: Props = $props();
  // svelte-ignore state_referenced_locally
  const uploader = createUploader({ target, onUploaded });
  const accept = $derived(acceptForCategory(categoryForTarget(target)));
  let input: HTMLInputElement | undefined = $state();
</script>

<input
  bind:this={input}
  type="file"
  multiple
  {accept}
  class="hidden"
  onchange={(event) => {
    const files = event.currentTarget.files;
    if (files?.length) void uploader.uploadFiles(files);
    event.currentTarget.value = "";
  }}
/>

<button
  type="button"
  onclick={() => input?.click()}
  {disabled}
  class="inline-flex items-center gap-1.5 border border-border-subtle bg-surface-2/70 px-3 py-1.5 text-[0.78rem] text-text-muted transition-colors hover:border-border-accent hover:text-text-primary disabled:opacity-50"
>
  <Upload class="h-3.5 w-3.5" />
  {label}
</button>

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
