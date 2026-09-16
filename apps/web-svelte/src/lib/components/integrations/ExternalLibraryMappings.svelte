<script lang="ts">
  import { onMount } from "svelte";
  import { Button, DialogBase, Panel, Select, TextInput, Toggle } from "@prismedia/ui-svelte";
  import type { ConnectionResponse, EntityKind, ExternalLibraryMount, ManagerOptions } from "$lib/api/generated/model";
  import { fetchLibraryMounts, fetchManagerOptions, saveLibraryMount } from "$lib/api/managed-libraries";
  import { SETTING_SECTION } from "$lib/settings/settings-section-catalog";

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
  let sequence = 0;
  onMount(() => {
    void fetchLibraryMounts(connection.id).then(value => { mounts = value; }).catch(cause => { error = cause.message; });
    return () => { sequence++; };
  });
  async function begin() {
    if (!kind) return;
    const current = ++sequence;
    open = true; busy = true; error = null; options = null; remoteRootId = ""; localPath = ""; label = `${connection.name} library`; isNsfw = false;
    try {
      const choices = await fetchManagerOptions(connection.id, kind);
      if (current !== sequence) return;
      options = choices;
      remoteRootId = choices.roots.find(root => !mounts.some(mount => mount.remoteRootId === root.id))?.id ?? "";
    } catch (cause) { if (current === sequence) error = cause instanceof Error ? cause.message : "Could not read remote folders"; }
    finally { if (current === sequence) busy = false; }
  }
  async function save() {
    const remote = options?.roots.find(root => root.id === remoteRootId);
    if (!kind || !remote) return;
    busy = true; error = null;
    try {
      const created = await saveLibraryMount(connection.id, { entityKind: kind, remoteRootId, expectedRemotePath: remote.path, localPath, label, isNsfw });
      mounts = [...mounts.filter(mount => mount.id !== created.id), created]; open = false;
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not map the folder"; }
    finally { busy = false; }
  }
</script>

<Panel class="space-y-3 p-4">
  <div class="flex flex-wrap items-center justify-between gap-2">
    <h2 class="text-sm font-semibold">Local library mappings</h2>
    <Button variant="outline" size="sm" disabled={!kind || busy} onclick={begin}>Map library folder</Button>
  </div>
  <p class="text-xs text-text-muted">Connect an existing folder visible to Prismedia. Files stay under the external application's control.</p>
  {#if error && !open}<p role="alert" class="text-sm text-error-text">{error}</p>{/if}
  {#each mounts as mount (mount.id)}
    <div class="space-y-1 border-t border-border-subtle pt-3 text-xs">
      <p class="font-medium">{mount.label} · read-only</p>
      <p class="break-all text-text-muted">Remote: {mount.remotePath}</p>
      <p class="break-all text-text-muted">Local: {mount.localPath}</p>
    </div>
  {/each}
  {#if mounts.length}<p class="text-xs text-text-muted">New mappings start paused. Enable scanning in <a class="underline" href={`/settings/${SETTING_SECTION.libraries}`}>Libraries</a> when ready.</p>{/if}
</Panel>

<DialogBase.Root {open} onOpenChange={value => { if (!busy) { open = value; sequence++; } }}>
  <DialogBase.Content class="sm:max-w-xl">
    <DialogBase.Header>
      <DialogBase.Title>Map an external library</DialogBase.Title>
      <DialogBase.Description>Use a dedicated folder already mounted on the Prismedia server. The mapping is fixed; scanning starts paused and can be enabled in Libraries.</DialogBase.Description>
    </DialogBase.Header>
    <form class="space-y-4" onsubmit={event => { event.preventDefault(); void save(); }}>
      {#if error}<p role="alert" class="text-sm text-error-text">{error}</p>{/if}
      {#if options && !options.roots.some(root => !mounts.some(mount => mount.remoteRootId === root.id))}
        <p class="text-sm text-text-muted">There are no unmapped root folders in this connection.</p>
      {/if}
      <label class="block space-y-1 text-sm">External folder
        <Select ariaLabel="External folder" value={remoteRootId} options={(options?.roots ?? []).filter(root => !mounts.some(mount => mount.remoteRootId === root.id)).map(root => ({ value: root.id, label: root.path }))} onchange={value => remoteRootId = value} disabled={busy} />
      </label>
      <label class="block space-y-1 text-sm">Local folder<TextInput bind:value={localPath} placeholder="/media/external-library" disabled={busy} required /></label>
      <label class="block space-y-1 text-sm">Library name<TextInput bind:value={label} disabled={busy} required /></label>
      <label class="flex items-center justify-between text-sm">NSFW library<Toggle ariaLabel="NSFW library" checked={isNsfw} onchange={value => isNsfw = value} disabled={busy} /></label>
      <DialogBase.Footer><Button type="button" variant="outline" disabled={busy} onclick={() => open = false}>Cancel</Button><Button type="submit" disabled={busy || !remoteRootId || !localPath.trim() || !label.trim()}>Create read-only library</Button></DialogBase.Footer>
    </form>
  </DialogBase.Content>
</DialogBase.Root>
