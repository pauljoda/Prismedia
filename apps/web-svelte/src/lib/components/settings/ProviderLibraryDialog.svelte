<script lang="ts">
  import { ExternalLink } from "@lucide/svelte";
  import { Button, ChoiceGroup, DialogBase, Select, TextInput, Toggle, type ChoiceOption } from "@prismedia/ui-svelte";
  import type { EntityKind, LibraryRoot, ProviderLibraryConnection, ProviderLibraryDescriptor } from "$lib/api/generated/model";
  import { attachExistingLibraryMount, fetchProviderLibraries, saveLibraryMount } from "$lib/api/managed-libraries";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import SharedStorageHelp from "$lib/components/integrations/SharedStorageHelp.svelte";

  import { rootScansKind } from "$lib/integrations/import-options";
  const MODE = { newFolder: "new-folder", existingLibrary: "existing-library" } as const;
  type MappingMode = typeof MODE[keyof typeof MODE];
  type ProviderChoice = { key: string; connection: ProviderLibraryConnection; library: ProviderLibraryDescriptor };

  interface Props {
    roots: LibraryRoot[];
    onComplete: () => void | Promise<void>;
    onError: (message: string) => void;
    onMessage: (message: string) => void;
  }

  let { roots, onComplete, onError, onMessage }: Props = $props();
  let open = $state(false);
  let busy = $state(false);
  let connections = $state<ProviderLibraryConnection[]>([]);
  let error = $state<string | null>(null);
  let providerKey = $state("");
  let entityKind = $state<EntityKind | "">("");
  let mode = $state<MappingMode>(MODE.newFolder);
  let existingLibraryRootId = $state("");
  let localPath = $state("");
  let label = $state("");
  let isNsfw = $state(false);

  const modeOptions: ChoiceOption<MappingMode>[] = [
    { value: MODE.newFolder, label: "Add mounted folder" },
    { value: MODE.existingLibrary, label: "Use existing library" },
  ];
  const providerChoices = $derived.by<ProviderChoice[]>(() => connections.flatMap(connection =>
    connection.libraries.map((library, index) => ({ key: `${connection.connectionId}:${index}`, connection, library }))));
  const selected = $derived(providerChoices.find(choice => choice.key === providerKey));
  const mappedKeys = $derived(new Set(roots.flatMap(root => root.externalOrigin
    ? [`${root.externalOrigin.connectionId}:${root.externalOrigin.remoteLibraryId}`]
    : [])));
  const availableChoices = $derived(providerChoices.filter(choice =>
    !mappedKeys.has(`${choice.connection.connectionId}:${choice.library.remoteId}`)));
  const compatibleRoots = $derived(roots.filter(root => !root.isReadOnly && entityKind && supports(root, entityKind)));
  const selectedRoot = $derived(compatibleRoots.find(root => root.id === existingLibraryRootId));

  function supports(root: LibraryRoot, kind: EntityKind): boolean {
    return rootScansKind(root, kind);
  }

  async function show() {
    open = true;
    busy = true;
    error = null;
    providerKey = "";
    entityKind = "";
    existingLibraryRootId = "";
    localPath = "";
    label = "";
    isNsfw = false;
    try {
      connections = await fetchProviderLibraries();
    } catch (cause) {
      error = cause instanceof Error ? cause.message : "Could not discover provider libraries";
    } finally {
      busy = false;
    }
  }

  function selectProvider(value: string) {
    providerKey = value;
    const choice = providerChoices.find(item => item.key === value);
    entityKind = choice?.library.entityKinds[0] ?? "";
    label = choice?.library.label ?? "";
    existingLibraryRootId = "";
  }

  async function save() {
    if (!selected || !entityKind) return;
    busy = true;
    error = null;
    try {
      if (mode === MODE.existingLibrary) {
        if (!selectedRoot) return;
        await attachExistingLibraryMount(selected.connection.connectionId, {
          entityKind,
          remoteRootId: selected.library.remoteId,
          expectedRemotePath: selected.library.remotePath,
          existingLibraryRootId: selectedRoot.id,
          expectedLocalPath: selectedRoot.path,
        });
      } else {
        await saveLibraryMount(selected.connection.connectionId, {
          entityKind,
          remoteRootId: selected.library.remoteId,
          expectedRemotePath: selected.library.remotePath,
          localPath,
          label,
          isNsfw,
        });
      }
      open = false;
      onMessage(`Added ${selected.library.label} from ${selected.connection.connectionName}.`);
      await onComplete();
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Could not add the provider library";
      error = message;
      onError(message);
    } finally {
      busy = false;
    }
  }
</script>

<Button type="button" variant="secondary" size="sm" onclick={() => void show()}>Add provider library</Button>

<DialogBase.Root {open} onOpenChange={value => { if (!busy) open = value; }}>
  <DialogBase.Content class="sm:max-w-2xl">
    <DialogBase.Header>
      <DialogBase.Title>Add provider library</DialogBase.Title>
      <DialogBase.Description>
        Add a provider's library to Prismedia for browsing and playback. The provider continues to organize its files.
      </DialogBase.Description>
    </DialogBase.Header>
    <form class="space-y-4" onsubmit={event => { event.preventDefault(); void save(); }}>
      {#if error}<p role="alert" class="text-sm text-status-error-text">{error}</p>{/if}
      {#if connections.some(connection => connection.error)}
        <div class="surface-well space-y-1 p-3 text-xs text-text-muted">
          {#each connections.filter(connection => connection.error) as connection (connection.connectionId)}
            <p><span class="text-text-primary">{connection.connectionName}:</span> {connection.error}</p>
          {/each}
        </div>
      {/if}
      <label class="block space-y-1 text-sm">Provider library
        <Select ariaLabel="Provider library" value={providerKey}
          options={availableChoices.map(choice => ({ value: choice.key, label: `${choice.connection.connectionName} · ${choice.library.label} · ${choice.library.remotePath}` }))}
          onchange={selectProvider} disabled={busy} placeholder="Choose a provider library" />
      </label>
      {#if selected}
        <div class="surface-well flex flex-wrap items-start justify-between gap-3 p-3 text-xs">
          <div class="min-w-0">
            <p class="font-medium text-text-primary">{selected.connection.connectionName}</p>
            <p class="break-all font-mono text-text-muted">Provider path: {selected.library.remotePath}</p>
          </div>
          <a class="inline-flex items-center gap-1 text-text-accent hover:underline" href={selected.library.managementUrl ?? `/settings/connections`} target={selected.library.managementUrl ? "_blank" : undefined} rel={selected.library.managementUrl ? "noreferrer" : undefined}>
            Manage provider <ExternalLink class="size-3.5" />
          </a>
        </div>
        {#if selected.library.entityKinds.length > 1}
          <label class="block space-y-1 text-sm">Library type
            <Select ariaLabel="Library type" value={entityKind}
              options={selected.library.entityKinds.map(kind => ({ value: kind, label: labelForEntityKind(kind) }))}
              onchange={value => { entityKind = value as EntityKind; existingLibraryRootId = ""; }} disabled={busy} />
          </label>
        {/if}
        <ChoiceGroup type="single" options={modeOptions} value={mode}
          onValueChange={value => { mode = value; existingLibraryRootId = ""; }} ariaLabel="Library mapping method" disabled={busy} />
        {#if mode === MODE.existingLibrary}
          <label class="block space-y-1 text-sm">Existing Prismedia library
            <Select ariaLabel="Existing Prismedia library" value={existingLibraryRootId}
              options={compatibleRoots.map(root => ({ value: root.id, label: `${root.label} · ${root.path}` }))}
              onchange={value => existingLibraryRootId = value} disabled={busy} placeholder="Choose a library" />
          </label>
          {#if compatibleRoots.length === 0}<p class="text-sm text-text-muted">No compatible writable library is available for this type.</p>{/if}
        {:else}
          <label class="block space-y-1 text-sm">Folder visible to Prismedia<TextInput bind:value={localPath} placeholder="/media/external" disabled={busy} required /></label>
          <label class="block space-y-1 text-sm">Library name<TextInput bind:value={label} disabled={busy} required /></label>
          <label class="flex items-center justify-between text-sm">NSFW library<Toggle ariaLabel="NSFW library" checked={isNsfw} onchange={value => isNsfw = value} disabled={busy} /></label>
          <p class="text-xs text-text-muted">Prismedia reads this folder in place. The provider continues to organize its files; no copy or move is needed.</p>
        {/if}
      {:else if !busy && availableChoices.length === 0}
        {#if providerChoices.length === 0}
          <p class="text-sm text-text-muted">No provider libraries were discovered. Check that the provider connection is enabled and exposes a library, then make its media folder readable by the Prismedia server through a mount or network share.</p>
        {:else}
          <p class="text-sm text-text-muted">Every discovered provider library is already linked.</p>
        {/if}
      {/if}
      {#if !busy && (availableChoices.length > 0 || providerChoices.length === 0)}
        <SharedStorageHelp providerName={selected?.connection.connectionName} />
      {/if}
      <DialogBase.Footer>
        <Button type="button" variant="outline" disabled={busy} onclick={() => open = false}>Cancel</Button>
        <Button type="submit" disabled={busy || !selected || !entityKind || (mode === MODE.existingLibrary ? !selectedRoot : !localPath.trim() || !label.trim())}>Add library</Button>
      </DialogBase.Footer>
    </form>
  </DialogBase.Content>
</DialogBase.Root>
