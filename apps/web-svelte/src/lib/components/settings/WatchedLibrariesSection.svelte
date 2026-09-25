<script lang="ts">
  import { invalidateAll } from "$app/navigation";
  import { ChevronLeft, FolderOpen, Loader2, Plus } from "@lucide/svelte";
  import { TextInput, Button, Panel } from "@prismedia/ui-svelte";
  import {
    browseLibraryPath,
    createLibraryRoot,
    deleteLibraryRoot,
    updateLibraryRoot,
    type LibraryBrowse,
    type LibraryRoot,
  } from "$lib/api/settings";
  import { createJob } from "$lib/api/jobs";
  import { JOB_TYPE } from "$lib/api/generated/codes";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useSession } from "$lib/stores/session.svelte";
  import { rescanFileRoot } from "$lib/api/files";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import LibraryAccessDialog from "./LibraryAccessDialog.svelte";
  import LibraryCard, { type LibraryFlag } from "./LibraryCard.svelte";
  import { entityTerms } from "$lib/terminology";
  import ToggleCard from "./ToggleCard.svelte";
  import ProviderLibraryDialog from "./ProviderLibraryDialog.svelte";

  interface Props {
    roots: LibraryRoot[];
    onRootsChanged: () => void | Promise<void>;
    onError: (msg: string) => void;
    onMessage: (msg: string) => void;
  }

  let { roots = $bindable(), onRootsChanged, onError, onMessage }: Props = $props();

  const session = useSession();
  let accessDialogRoot = $state<LibraryRoot | null>(null);
  let removeDialogRoot = $state<LibraryRoot | null>(null);
  let scanningRootId = $state<string | null>(null);

  const nsfw = useNsfw();

  let loading = $state(false);
  let browser = $state<LibraryBrowse | null>(null);
  let browserVisible = $state(false);
  let addingRoot = $state(false);
  let newRootPath = $state("");
  let newRootLabel = $state("");
  let newRootRecursive = $state(true);
  let newRootScanVideos = $state(true);
  let newRootScanImages = $state(true);
  let newRootScanAudio = $state(true);
  let newRootScanBooks = $state(false);
  let newRootIsNsfw = $state(false);
  let newRootAutoIdentify = $state(true);

  const rootsVisible = $derived.by(() => {
    if (nsfw.mode === "off") return roots.filter((r) => !r.isNsfw);
    return roots;
  });


  async function openBrowser(targetPath?: string) {
    try {
      const response = await browseLibraryPath(targetPath);
      browser = response;
      browserVisible = true;
      newRootPath = response.path;
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to browse folders");
    }
  }

  async function handleAddRoot() {
    if (!newRootPath.trim()) {
      onError("Choose a folder before adding a library root.");
      return;
    }
    addingRoot = true;
    try {
      await createLibraryRoot({
        path: newRootPath,
        label: newRootLabel || undefined,
        recursive: newRootRecursive,
        scanVideos: newRootScanVideos,
        scanImages: newRootScanImages,
        scanAudio: newRootScanAudio,
        scanBooks: newRootScanBooks,
        isNsfw: newRootIsNsfw,
        autoIdentify: newRootAutoIdentify,
      });
      onMessage("Library root added.");
      newRootPath = "";
      newRootLabel = "";
      newRootIsNsfw = false;
      newRootAutoIdentify = true;
      browserVisible = false;
      await onRootsChanged();
      await invalidateAll();
      await createJob(JOB_TYPE.scanLibrary);
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to add library root");
    } finally {
      addingRoot = false;
    }
  }

  /** Flips one library switch optimistically, restoring it when the server refuses. */
  async function handleToggle(root: LibraryRoot, flag: LibraryFlag) {
    const next = !root[flag];
    const apply = (value: boolean) =>
      (roots = roots.map((entry) => (entry.id === root.id ? { ...entry, [flag]: value } : entry)));
    apply(next);
    try {
      await updateLibraryRoot(root.id, { [flag]: next });
      await invalidateAll();
    } catch (err) {
      apply(!next);
      onError(err instanceof Error ? err.message : "Failed to update library");
    }
  }

  async function handleScanRoot(root: LibraryRoot) {
    scanningRootId = root.id;
    try {
      await rescanFileRoot({ rootId: root.id, path: null });
      onMessage(`Scanning ${root.label}.`);
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to start the scan");
    } finally {
      scanningRootId = null;
    }
  }

  async function handleDeleteRoot(root: LibraryRoot) {
    try {
      await deleteLibraryRoot(root.id);
      onMessage(`Removed ${root.label}.`);
      await onRootsChanged();
      await invalidateAll();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to remove root");
    } finally {
      removeDialogRoot = null;
    }
  }
</script>

<Panel>
  <div class="p-5 space-y-5">
  <div class="flex flex-wrap items-center justify-between gap-3">
    <div class="flex items-center gap-2.5">
      <FolderOpen class="h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-kicker text-text-primary">Watched Libraries</h2>
        <p class="text-[0.68rem] text-text-muted">
          Add mounted folders to scan for media files
        </p>
      </div>
    </div>
    <div class="flex flex-wrap items-center gap-2">
      {#if session.isAdmin}<ProviderLibraryDialog {roots} onComplete={onRootsChanged} {onError} {onMessage} />{/if}
      <Button
        type="button"
        variant="secondary"
        size="sm"
        onclick={() => void openBrowser(browser?.path)}
        class="no-lift gap-1.5 px-3 py-1.5 text-xs"
      >
        <Plus class="h-3.5 w-3.5" />
        Browse Folder
      </Button>
    </div>
  </div>

  {#if browserVisible}
    <div class="surface-card no-lift space-y-4 border-border-accent/30 p-4">
      <div class="surface-well p-3 border border-border-subtle">
        <div class="mb-3 flex items-center gap-2">
          <Button variant="outline" size="sm"
            type="button"
            onclick={() => void openBrowser(browser?.parentPath ?? browser?.path)}
            disabled={!browser?.parentPath}
            class="flex items-center gap-1 flex-shrink-0 px-2.5 py-1.5 text-xs font-medium disabled:opacity-40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-500/25"
          >
            <ChevronLeft class="h-3.5 w-3.5" />
            Up
          </Button>
          <div
            class="flex-1 overflow-x-auto scrollbar-hidden rounded-xs border border-border-subtle bg-surface-1 px-3 py-1.5 shadow-well"
          >
            <span class="whitespace-nowrap text-mono-sm text-text-accent">
              {browser?.path ?? "Loading..."}
            </span>
          </div>
        </div>
        <div
          class="scrollbar-hidden grid max-h-[260px] gap-1.5 overflow-y-auto md:grid-cols-2"
        >
          {#if browser}
            {#each browser.directories as directory (directory.path)}
              <Button variant="outline" size="sm"
                type="button"
                class="surface-card px-3 py-2 text-left flex items-center gap-3 group hover:border-border-accent/50 transition-colors h-auto whitespace-normal"
                onclick={() => void openBrowser(directory.path)}
              >
                <FolderOpen
                  class="h-4 w-4 text-text-disabled group-hover:text-text-accent transition-colors flex-shrink-0"
                />
                <div class="min-w-0 flex-1">
                  <p
                    class="truncate text-[0.8rem] font-medium group-hover:text-text-primary transition-colors"
                  >
                    {directory.name}
                  </p>
                </div>
              </Button>
            {/each}
            {#if browser.directories.length === 0}
              <p
                class="empty-rack-slot col-span-full py-6 text-center text-xs text-text-disabled"
              >
                No child directories found.
              </p>
            {/if}
          {/if}
        </div>
      </div>

      <div class="space-y-3">
        <div class="flex flex-col gap-1.5">
          <label class="control-label" for="new-root-label">Label (optional)</label>
          <TextInput
            id="new-root-label"
            class="w-full max-w-md py-1.5 text-sm"
            bind:value={newRootLabel}
            placeholder={`Primary ${entityTerms.videos.toLowerCase()}`}
          />
        </div>

        <div class="space-y-2" role="group" aria-labelledby="library-options-heading">
          <div id="library-options-heading" class="control-label">Library Options</div>
          <div class="grid gap-2 md:grid-cols-2 lg:grid-cols-3">
            <ToggleCard
              label="Recursive"
              description="Scan all subfolders"
              checked={newRootRecursive}
              onChange={(v) => (newRootRecursive = v)}
            />
            <ToggleCard
              label="Videos"
              description="Scan video files"
              checked={newRootScanVideos}
              onChange={(v) => (newRootScanVideos = v)}
            />
            <ToggleCard
              label="Images"
              description="Scan image files"
              checked={newRootScanImages}
              onChange={(v) => (newRootScanImages = v)}
            />
            <ToggleCard
              label="Audio"
              description="Scan audio files"
              checked={newRootScanAudio}
              onChange={(v) => (newRootScanAudio = v)}
            />
            <ToggleCard
              label="Books"
              description="Scan ZIP/CBZ comic archives"
              checked={newRootScanBooks}
              onChange={(v) => (newRootScanBooks = v)}
            />
            {#if session.allowNsfw}
              <ToggleCard
                label="NSFW"
                description="Mark content as adult"
                checked={newRootIsNsfw}
                onChange={(v) => (newRootIsNsfw = v)}
              />
            {/if}
            <ToggleCard
              label="Auto Identify"
              description="Include in automatic identification"
              checked={newRootAutoIdentify}
              onChange={(v) => (newRootAutoIdentify = v)}
            />
          </div>
        </div>

        <div class="flex items-center gap-3 pt-2">
          <Button
            type="button"
            variant="primary"
            size="sm"
            onclick={() => void handleAddRoot()}
            disabled={addingRoot || !newRootPath}
            class="gap-1.5 px-4 py-2 text-xs"
          >
            {#if addingRoot}
              <Loader2 class="h-3.5 w-3.5 shrink-0 animate-spin" />
            {:else}
              <Plus class="h-3.5 w-3.5" />
            {/if}
            {addingRoot ? "Adding..." : "Add Library"}
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onclick={() => {
              browserVisible = false;
              newRootPath = "";
              newRootLabel = "";
            }}
            class="px-3 py-2 text-xs"
          >
            Cancel
          </Button>
        </div>
      </div>
    </div>
  {/if}

  {#if loading}
    <StatePlaceholder icon={FolderOpen} title="Loading libraries" busy />
  {:else if roots.length === 0}
    <StatePlaceholder icon={FolderOpen} title="No libraries yet" />
  {:else if rootsVisible.length === 0}
    <StatePlaceholder icon={FolderOpen} title="No libraries to show" />
  {:else}
    <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
      {#each rootsVisible as root (root.id)}
        <LibraryCard
          {root}
          scanning={scanningRootId === root.id}
          canManageAccess={session.isAdmin}
          showNsfw={session.allowNsfw}
          onToggle={(target, flag) => void handleToggle(target, flag)}
          onScan={(target) => void handleScanRoot(target)}
          onAccess={(target) => (accessDialogRoot = target)}
          onRemove={(target) => (removeDialogRoot = target)}
        />
      {/each}
    </div>
  {/if}
  </div>
</Panel>

{#if accessDialogRoot}
  <LibraryAccessDialog
    open={accessDialogRoot !== null}
    rootId={accessDialogRoot.id}
    rootLabel={accessDialogRoot.label}
    onSaved={() => {
      onMessage("Library access saved.");
      accessDialogRoot = null;
    }}
    onClose={() => (accessDialogRoot = null)}
  />
{/if}

<ConfirmDialog
  open={removeDialogRoot !== null}
  title="Remove {removeDialogRoot?.label ?? 'library'}"
  message="Its items leave Prismedia. Files on disk stay where they are."
  confirmLabel="Remove"
  danger
  onConfirm={() => (removeDialogRoot ? handleDeleteRoot(removeDialogRoot) : undefined)}
  onClose={() => (removeDialogRoot = null)}
/>
