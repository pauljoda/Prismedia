<script lang="ts">
  import { Image, LoaderCircle, Trash2, Upload } from "@lucide/svelte";
  import { Button, Disclosure, Item } from "@prismedia/ui-svelte";
  import type { EntityFileRoleCode } from "$lib/entities/entity-codes";

  interface ArtworkOption {
    role: EntityFileRoleCode;
    label: string;
    hasAsset: boolean;
  }

  let { assets, busyRole = null, onUpload, onClear }: {
    assets: ArtworkOption[];
    busyRole?: EntityFileRoleCode | null;
    onUpload: (role: EntityFileRoleCode, file: File) => void | Promise<void>;
    onClear: (role: EntityFileRoleCode) => void | Promise<void>;
  } = $props();

  let fileInput = $state<HTMLInputElement | null>(null);
  let selectedRole: EntityFileRoleCode | null = null;

  function pickFile(role: EntityFileRoleCode) {
    selectedRole = role;
    fileInput?.click();
  }

  function uploadFile(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    input.value = "";
    if (file && selectedRole) void onUpload(selectedRole, file);
  }
</script>

<section aria-label="Artwork" class="min-w-0 bg-surface-1 px-6 py-4 sm:px-8">
  <input bind:this={fileInput} type="file" accept="image/*" class="hidden" aria-label="Artwork file" onchange={uploadFile} disabled={busyRole !== null} />
  <Disclosure title="Edit artwork" icon={Image}>
  <div class="@container flex min-w-0 flex-col gap-3">
  <p class="text-caption text-muted-foreground">Artwork changes save immediately.</p>
  <Item.Group class="grid min-w-0 gap-4 @min-[48rem]:grid-cols-2">
    {#each assets as asset (asset.role)}
      <Item.Root class="@container min-w-0 p-0" aria-label={`${asset.label} image`} role="group" aria-busy={busyRole === asset.role}>
        <Item.Media variant="icon"><Image /></Item.Media>
        <Item.Content class="min-w-0">
          <Item.Title>{asset.label}</Item.Title>
          {#if busyRole === asset.role}
            <Item.Description role="status">Updating {asset.label.toLowerCase()}…</Item.Description>
          {:else if !asset.hasAsset}
            <Item.Description>No image</Item.Description>
          {/if}
        </Item.Content>
        <Item.Actions class="flex-wrap @max-[26rem]:w-full @max-[26rem]:[&>button]:flex-1">
          <Button variant="secondary" aria-label={`${asset.hasAsset ? 'Replace' : 'Upload'} ${asset.label.toLowerCase()}`} disabled={busyRole !== null} onclick={() => pickFile(asset.role)}>
            {#if busyRole === asset.role}<LoaderCircle class="animate-spin motion-reduce:animate-none" data-icon="inline-start" />{:else}<Upload data-icon="inline-start" />{/if}
            {asset.hasAsset ? "Replace" : "Upload"}
          </Button>
          {#if asset.hasAsset}
            <Button variant="ghost" aria-label={`Remove ${asset.label.toLowerCase()}`} disabled={busyRole !== null} onclick={() => void onClear(asset.role)}>
              <Trash2 data-icon="inline-start" />Remove
            </Button>
          {/if}
        </Item.Actions>
      </Item.Root>
    {/each}
  </Item.Group>
  </div>
  </Disclosure>
</section>
