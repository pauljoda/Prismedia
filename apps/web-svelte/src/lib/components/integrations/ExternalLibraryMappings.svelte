<script lang="ts">
  import { onMount } from "svelte";
  import { Button, ChoiceGroup, DialogBase, Panel, Select, TextInput, Toggle, type ChoiceOption } from "@prismedia/ui-svelte";
  import { ENTITY_KIND } from "$lib/api/generated/codes";
  import type { ConnectionResponse, EntityKind, ExternalLibraryMount, LibraryRoot, ManagerOptions } from "$lib/api/generated/model";
  import { attachExistingLibraryMount, fetchLibraryMounts, fetchManagerOptions, saveLibraryMount } from "$lib/api/managed-libraries";
  import { fetchLibraryRoots } from "$lib/api/settings";
  import { SETTING_SECTION } from "$lib/settings/settings-section-catalog";
  import SharedStorageHelp from "./SharedStorageHelp.svelte";

  const MAPPING_MODE = { newFolder: "new-folder", existingLibrary: "existing-library" } as const;
  type MappingMode = typeof MAPPING_MODE[keyof typeof MAPPING_MODE];
  const mappingModeOptions: ChoiceOption<MappingMode>[] = [
    { value: MAPPING_MODE.newFolder, label: "Add mounted folder" },
    { value: MAPPING_MODE.existingLibrary, label: "Use existing library" },
  ];

  function supportsKind(root: LibraryRoot, entityKind: EntityKind | undefined): boolean {
    if (!entityKind || root.isReadOnly) return false;
    if (entityKind === ENTITY_KIND.movie || entityKind === ENTITY_KIND.videoSeries) return root.scanVideos;
    if (entityKind === ENTITY_KIND.book || entityKind === ENTITY_KIND.comicSeries) return root.scanBooks;
    if (entityKind === ENTITY_KIND.image || entityKind === ENTITY_KIND.gallery) return root.scanImages;
    return entityKind === ENTITY_KIND.audioLibrary && root.scanAudio;
  }

  let { connection, kind }: { connection: ConnectionResponse; kind: EntityKind | undefined } = $props();
  let mounts = $state<ExternalLibraryMount[]>([]);
  let options = $state<ManagerOptions | null>(null);
  let open = $state(false);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let remoteRootId = $state("");
  let localPath = $state("");
  let label = $state("");
  let isNsfw = $state(false);
  let libraryRoots = $state<LibraryRoot[]>([]);
  let mappingMode = $state<MappingMode>(MAPPING_MODE.newFolder);
  let existingLibraryRootId = $state("");
  let sequence = 0;
  const availableExistingRoots = $derived(libraryRoots.filter(root => supportsKind(root, kind) && !mounts.some(mount => mount.libraryRootId === root.id)));
  const selectedExistingRoot = $derived(availableExistingRoots.find(root => root.id === existingLibraryRootId));
  onMount(() => {
    void fetchLibraryMounts(connection.id).then(value => { mounts = value; }).catch(cause => { error = cause.message; });
    return () => { sequence++; };
  });
  async function begin() {
    if (!kind) return;
    const current = ++sequence;
    open = true; busy = true; error = null; options = null; remoteRootId = ""; localPath = ""; label = `${connection.name} library`; isNsfw = false; mappingMode = MAPPING_MODE.newFolder; existingLibraryRootId = "";
    try {
      const [choices, roots, currentMounts] = await Promise.all([
        fetchManagerOptions(connection.id, kind),
        fetchLibraryRoots(),
        fetchLibraryMounts(connection.id),
      ]);
      if (current !== sequence) return;
      options = choices; libraryRoots = roots; mounts = currentMounts;
      remoteRootId = choices.roots.find(root => !mounts.some(mount => mount.remoteRootId === root.id))?.id ?? "";
    } catch (cause) { if (current === sequence) error = cause instanceof Error ? cause.message : "Could not read folders reported by the provider. Check its API connection and try again."; }
    finally { if (current === sequence) busy = false; }
  }
  async function save() {
    const remote = options?.roots.find(root => root.id === remoteRootId);
    if (!kind || !remote) return;
    busy = true; error = null;
    try {
      const created = mappingMode === MAPPING_MODE.existingLibrary
        ? selectedExistingRoot
          ? await attachExistingLibraryMount(connection.id, {
            entityKind: kind,
            remoteRootId,
            expectedRemotePath: remote.path,
            existingLibraryRootId: selectedExistingRoot.id,
            expectedLocalPath: selectedExistingRoot.path,
          })
          : null
        : await saveLibraryMount(connection.id, { entityKind: kind, remoteRootId, expectedRemotePath: remote.path, localPath, label, isNsfw });
      if (!created) return;
      mounts = [...mounts.filter(mount => mount.id !== created.id), created]; open = false;
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not map the folder"; }
    finally { busy = false; }
  }
</script>

<Panel class="space-y-3 p-4">
  <div class="flex flex-wrap items-center justify-between gap-2">
    <h2 class="text-sm font-semibold">Shared folder mappings</h2>
    <Button variant="outline" size="sm" disabled={!kind || busy} onclick={begin}>Map shared folder</Button>
  </div>
  <p class="text-xs text-text-muted">Map a path reported by {connection.name} to the same media folder already mounted on and readable by the Prismedia server.</p>
  {#if error && !open}<p role="alert" class="text-sm text-error-text">{error}</p>{/if}
  {#each mounts as mount (mount.id)}
    <div class="space-y-1 border-t border-border-subtle pt-3 text-xs">
      <p class="font-medium">{mount.label} · read-only</p>
      <p class="break-all text-text-muted">Provider path: {mount.remotePath}</p>
      <p class="break-all text-text-muted">Folder visible to Prismedia: {mount.localPath}</p>
    </div>
  {/each}
  {#if mounts.length}<p class="text-xs text-text-muted">Newly added libraries start paused; linked existing libraries keep their scan settings. Manage scanning in <a class="underline" href={`/settings/${SETTING_SECTION.libraries}`}>Libraries</a>.</p>{/if}
</Panel>

<DialogBase.Root {open} onOpenChange={value => { if (!busy) { open = value; sequence++; } }}>
  <DialogBase.Content class="sm:max-w-xl">
    <DialogBase.Header>
      <DialogBase.Title>Map an external library</DialogBase.Title>
      <DialogBase.Description>
        {#if mappingMode === MAPPING_MODE.existingLibrary}
          Keep this library's files, entries, and settings. {connection.name} organizes the files; Prismedia reads them through the server's existing mount or share.
        {:else}
          Choose a dedicated folder already mounted or shared with the Prismedia server. The mapping is fixed; new mappings start paused and can be enabled in Libraries.
        {/if}
      </DialogBase.Description>
    </DialogBase.Header>
    <form class="space-y-4" onsubmit={event => { event.preventDefault(); void save(); }}>
      {#if error}<p role="alert" class="text-sm text-error-text">{error}</p>{/if}
      {#if options && !options.roots.some(root => !mounts.some(mount => mount.remoteRootId === root.id))}
        <p class="text-sm text-text-muted">There are no unmapped folders reported by {connection.name}. Refresh its API connection if a provider folder is missing.</p>
      {/if}
      <ChoiceGroup type="single" options={mappingModeOptions} value={mappingMode}
        onValueChange={value => { mappingMode = value; existingLibraryRootId = ""; }} ariaLabel="Library mapping method" disabled={busy} />
      <label class="block space-y-1 text-sm">Path reported by {connection.name}
        <Select ariaLabel={`Path reported by ${connection.name}`} value={remoteRootId} options={(options?.roots ?? []).filter(root => !mounts.some(mount => mount.remoteRootId === root.id)).map(root => ({ value: root.id, label: root.path }))} onchange={value => remoteRootId = value} disabled={busy} />
      </label>
      {#if mappingMode === MAPPING_MODE.existingLibrary}
        <div class="space-y-2">
          <label class="block space-y-1 text-sm">Existing Prismedia library
            <Select ariaLabel="Existing Prismedia library" value={existingLibraryRootId}
              options={availableExistingRoots.map(root => ({ value: root.id, label: `${root.label} · ${root.path}` }))}
              onchange={value => existingLibraryRootId = value} disabled={busy} placeholder="Choose a library" />
          </label>
          {#if selectedExistingRoot}
            <p class="text-xs text-text-muted">This folder becomes read-only to Prismedia. Its files stay at <span class="break-all font-mono">{selectedExistingRoot.path}</span>, and scanning keeps its current settings.</p>
          {:else if !availableExistingRoots.length}
            <p class="text-sm text-text-muted">No compatible writable library is available for this media type.</p>
          {/if}
        </div>
      {:else}
        <label class="block space-y-1 text-sm">Folder visible to Prismedia<TextInput bind:value={localPath} placeholder="/media/external" disabled={busy} required /></label>
        <label class="block space-y-1 text-sm">Library name<TextInput bind:value={label} disabled={busy} required /></label>
        <label class="flex items-center justify-between text-sm">NSFW library<Toggle ariaLabel="NSFW library" checked={isNsfw} onchange={value => isNsfw = value} disabled={busy} /></label>
      {/if}
      <SharedStorageHelp providerName={connection.name} />
      <DialogBase.Footer><Button type="button" variant="outline" disabled={busy} onclick={() => open = false}>Cancel</Button><Button type="submit" disabled={busy || !remoteRootId || (mappingMode === MAPPING_MODE.existingLibrary ? !selectedExistingRoot : !localPath.trim() || !label.trim())}>{mappingMode === MAPPING_MODE.existingLibrary ? "Link existing library" : "Create read-only library"}</Button></DialogBase.Footer>
    </form>
  </DialogBase.Content>
</DialogBase.Root>
