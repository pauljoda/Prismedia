<script lang="ts">
  import { onMount } from "svelte";
  import { FolderOpen, Plus, ShieldUser } from "@lucide/svelte";
  import { Alert, buttonVariants } from "@prismedia/ui-svelte";
  import { rescanFileRoot } from "$lib/api/files";
  import { deleteLibraryRoot, fetchLibraryConfig, updateLibraryRoot, type LibraryRoot } from "$lib/api/settings";
  import BackLink from "$lib/components/BackLink.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import LibraryCard, { type LibraryFlag } from "$lib/components/concepts/libraries/LibraryCard.svelte";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import ManagePageHeader from "$lib/components/manage/ManagePageHeader.svelte";
  import LibraryAccessDialog from "$lib/components/settings/LibraryAccessDialog.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();
  const nsfw = useNsfw();

  let roots = $state.raw<LibraryRoot[] | null>(null);
  let error = $state<string | null>(null);
  let message = $state<string | null>(null);
  let scanningId = $state<string | null>(null);
  let accessRoot = $state<LibraryRoot | null>(null);
  let removeRoot = $state<LibraryRoot | null>(null);

  async function load() {
    try {
      roots = (await fetchLibraryConfig()).roots;
    } catch (cause) {
      roots = [];
      error = cause instanceof Error ? cause.message : "Could not load libraries";
    }
  }

  onMount(() => {
    if (session.isAdmin) void load();
  });

  const visible = $derived((roots ?? []).filter((root) => nsfw.mode !== "off" || !root.isNsfw));
  const disabledCount = $derived(visible.filter((root) => !root.enabled).length);
  const externalCount = $derived(visible.filter((root) => root.externalOrigin).length);

  async function toggle(root: LibraryRoot, flag: LibraryFlag) {
    const next = !root[flag];
    const apply = (value: boolean) =>
      (roots = (roots ?? []).map((entry) => (entry.id === root.id ? { ...entry, [flag]: value } : entry)));
    apply(next);
    try {
      await updateLibraryRoot(root.id, { [flag]: next });
    } catch (cause) {
      apply(!next);
      error = cause instanceof Error ? cause.message : "Could not update library";
    }
  }

  async function scan(root: LibraryRoot) {
    scanningId = root.id;
    try {
      await rescanFileRoot({ rootId: root.id, path: null });
      message = `Scanning ${root.label}`;
    } catch (cause) {
      error = cause instanceof Error ? cause.message : "Could not start the scan";
    } finally {
      scanningId = null;
    }
  }

  async function remove(root: LibraryRoot) {
    await deleteLibraryRoot(root.id);
    message = `Removed ${root.label}`;
    removeRoot = null;
    await load();
  }
</script>

<svelte:head><title>Library cards · Concepts · Prismedia</title></svelte:head>

<div class="mx-auto flex w-full max-w-7xl min-w-0 flex-col gap-6 pb-16">
  <BackLink fallback="/concepts" label="Concepts" variant="text" />

  {#if !session.isAdmin}
    <StatePlaceholder icon={ShieldUser} title="Administrator access required" />
  {:else}
    <ManagePageHeader icon={FolderOpen} title="Libraries">
      {#snippet status()}
        {#if roots}
          <span class="flex flex-wrap gap-x-3 font-mono text-[0.72rem] text-text-muted">
            <span>{visible.length} libraries</span>
            {#if externalCount > 0}<span>{externalCount} external</span>{/if}
            {#if disabledCount > 0}<span>{disabledCount} disabled</span>{/if}
          </span>
        {/if}
      {/snippet}
      {#snippet actions()}
        <a href="/settings/libraries" class={buttonVariants({ variant: "secondary", size: "sm" })}>
          <Plus aria-hidden="true" />
          Add library
        </a>
      {/snippet}
    </ManagePageHeader>

    {#if error}
      <Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>
    {:else if message}
      <Alert.Root role="status"><Alert.Description>{message}</Alert.Description></Alert.Root>
    {/if}

    {#if roots === null}
      <StatePlaceholder icon={FolderOpen} title="Loading libraries" busy />
    {:else if visible.length === 0}
      <StatePlaceholder icon={FolderOpen} title="No libraries" />
    {:else}
      <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {#each visible as root (root.id)}
          <LibraryCard
            {root}
            scanning={scanningId === root.id}
            canManageAccess={session.isAdmin}
            onToggle={(target, flag) => void toggle(target, flag)}
            onScan={(target) => void scan(target)}
            onAccess={(target) => (accessRoot = target)}
            onRemove={(target) => (removeRoot = target)}
          />
        {/each}
      </div>
    {/if}
  {/if}
</div>

{#if accessRoot}
  <LibraryAccessDialog
    open
    rootId={accessRoot.id}
    rootLabel={accessRoot.label}
    onSaved={() => (accessRoot = null)}
    onClose={() => (accessRoot = null)}
  />
{/if}

<ConfirmDialog
  open={removeRoot !== null}
  title="Remove {removeRoot?.label ?? 'library'}"
  message="Its items leave Prismedia. Files on disk stay where they are."
  confirmLabel="Remove"
  danger
  onConfirm={() => (removeRoot ? remove(removeRoot) : undefined)}
  onClose={() => (removeRoot = null)}
/>
