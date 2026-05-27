<script lang="ts">
  import { invalidateAll } from "$app/navigation";
  import {
    ChevronLeft,
    Clock,
    Eye,
    Film,
    FolderOpen,
    Image as ImageIcon,
    BookOpen,
    Loader2,
    Music,
    Plus,
    ToggleLeft,
    ToggleRight,
    Trash2,
  } from "@lucide/svelte";
  import { Button, Panel, StatusLed, cn } from "@prismedia/ui-svelte";
  import {
    browseLibraryPath,
    createLibraryRoot,
    deleteLibraryRoot,
    updateLibraryRoot,
    type LibraryBrowse,
    type LibraryRoot,
  } from "$lib/api/settings";
  import { createJob } from "$lib/api/prismedia";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { entityTerms } from "$lib/terminology";
  import ToggleCard from "./ToggleCard.svelte";

  interface Props {
    roots: LibraryRoot[];
    onRootsChanged: () => void | Promise<void>;
    onError: (msg: string) => void;
    onMessage: (msg: string) => void;
  }

  let { roots = $bindable(), onRootsChanged, onError, onMessage }: Props = $props();

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

  const rootsVisible = $derived.by(() => {
    if (nsfw.mode === "off") return roots.filter((r) => !r.isNsfw);
    return roots;
  });

  function formatTimestamp(value: string | null) {
    if (!value) return "Never";
    return new Date(value).toLocaleString();
  }

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
      });
      onMessage("Library root added.");
      newRootPath = "";
      newRootLabel = "";
      newRootIsNsfw = false;
      browserVisible = false;
      await onRootsChanged();
      await invalidateAll();
      await createJob("scan-library");
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to add library root");
    } finally {
      addingRoot = false;
    }
  }

  async function handleToggleRoot(root: LibraryRoot) {
    const next = !root.enabled;
    roots = roots.map((r) => (r.id === root.id ? { ...r, enabled: next } : r));
    try {
      await updateLibraryRoot(root.id, { enabled: next });
      await invalidateAll();
    } catch (err) {
      roots = roots.map((r) => (r.id === root.id ? { ...r, enabled: !next } : r));
      onError(err instanceof Error ? err.message : "Failed to update root");
    }
  }

  async function handleToggleMediaType(
    root: LibraryRoot,
    field: "scanVideos" | "scanImages" | "scanAudio" | "scanBooks",
  ) {
    const next = !root[field];
    roots = roots.map((r) => (r.id === root.id ? { ...r, [field]: next } : r));
    try {
      await updateLibraryRoot(root.id, { [field]: next });
      await invalidateAll();
    } catch (err) {
      roots = roots.map((r) => (r.id === root.id ? { ...r, [field]: !next } : r));
      onError(err instanceof Error ? err.message : "Failed to update root");
    }
  }

  async function handleToggleNsfw(root: LibraryRoot) {
    const next = !root.isNsfw;
    roots = roots.map((r) => (r.id === root.id ? { ...r, isNsfw: next } : r));
    try {
      await updateLibraryRoot(root.id, { isNsfw: next });
      await invalidateAll();
    } catch {
      roots = roots.map((r) => (r.id === root.id ? { ...r, isNsfw: !next } : r));
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

  {#if browserVisible}
    <div class="surface-card no-lift space-y-4 border-border-accent/30 p-4">
      <div class="surface-well p-3 border border-border-subtle">
        <div class="mb-3 flex items-center gap-2">
          <button
            type="button"
            onclick={() => void openBrowser(browser?.parentPath ?? browser?.path)}
            disabled={!browser?.parentPath}
            class="flex items-center gap-1 flex-shrink-0 rounded-xs border border-border-subtle px-2.5 py-1.5 text-xs font-medium text-text-muted transition-all hover:bg-surface-3 hover:text-text-primary disabled:opacity-40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-500/25"
          >
            <ChevronLeft class="h-3.5 w-3.5" />
            Up
          </button>
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
              <button
                type="button"
                class="surface-card px-3 py-2 text-left flex items-center gap-3 group hover:border-border-accent/50 transition-colors"
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
              </button>
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
          <input
            id="new-root-label"
            class="control-input w-full max-w-md py-1.5 text-sm"
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
            <ToggleCard
              label="NSFW"
              description="Mark content as adult"
              checked={newRootIsNsfw}
              onChange={(v) => (newRootIsNsfw = v)}
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
              <StatusLed status="accent" size="sm" pulse class="shrink-0" />
              <Loader2
                class="h-3.5 w-3.5 shrink-0 animate-spin text-accent-300 drop-shadow-[0_0_6px_rgba(199,155,92,0.35)]"
              />
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
    <div class="surface-card no-lift flex flex-col items-center justify-center gap-3 p-8">
      <div class="flex items-center gap-2">
        <StatusLed status="accent" pulse />
        <Loader2
          class="h-5 w-5 animate-spin text-accent-400 drop-shadow-[0_0_8px_rgba(199,155,92,0.3)]"
        />
      </div>
      <span class="text-mono-sm text-text-muted">Loading library configuration…</span>
    </div>
  {:else if roots.length === 0}
    <div class="empty-rack-slot flex flex-col items-center p-8 text-center">
      <FolderOpen class="mx-auto mb-2 h-8 w-8 text-text-disabled" />
      <p class="text-sm text-text-muted">
        No library roots configured. Browse to a mounted folder to begin.
      </p>
    </div>
  {:else if rootsVisible.length === 0}
    <div class="empty-rack-slot flex flex-col items-center p-8 text-center">
      <FolderOpen class="mx-auto mb-2 h-8 w-8 text-text-disabled" />
      <p class="text-sm text-text-muted">No library roots to display.</p>
    </div>
  {:else}
    <div class="space-y-2">
      {#each rootsVisible as root (root.id)}
        <div
          class={cn(
            "surface-card no-lift flex flex-col gap-3 p-4 transition-opacity duration-fast",
            !root.enabled && "opacity-50",
          )}
        >
          <div class="flex items-start justify-between gap-4">
            <div class="min-w-0 flex items-start gap-3">
              <div
                class={cn("led mt-1.5 flex-shrink-0", root.enabled ? "led-active" : "led-idle")}
              ></div>
              <div class="min-w-0">
                <h3 class="text-[0.85rem] font-semibold text-text-primary truncate">
                  {root.label}
                </h3>
                <p
                  class="mt-1.5 truncate text-mono-sm text-text-disabled bg-surface-1/50 rounded-xs border border-border-subtle px-2 py-0.5 inline-block max-w-full shadow-sm"
                >
                  {root.path}
                </p>
              </div>
            </div>
            <div class="flex items-center gap-1 shrink-0">
              <button
                type="button"
                onclick={() => void handleToggleRoot(root)}
                class="rounded-xs p-1.5 text-text-muted hover:text-text-primary hover:bg-surface-2 transition-colors"
                title={root.enabled ? "Disable Library" : "Enable Library"}
              >
                {#if root.enabled}
                  <ToggleRight class="h-4 w-4 text-text-accent" />
                {:else}
                  <ToggleLeft class="h-4 w-4" />
                {/if}
              </button>
              <button
                type="button"
                onclick={() => void handleDeleteRoot(root)}
                class="rounded-xs p-1.5 text-text-muted transition-colors hover:bg-error-muted/30 hover:text-error-text"
                title="Remove Library"
              >
                <Trash2 class="h-4 w-4" />
              </button>
            </div>
          </div>

          <div
            class="flex flex-wrap items-center justify-between gap-3 pt-3 border-t border-border-subtle/50"
          >
            <div class="flex flex-wrap items-center gap-2">
              <span
                class="text-[0.65rem] font-medium text-text-muted uppercase tracking-wider mr-1 hidden sm:inline-block"
              >
                Scans:
              </span>

              <button
                type="button"
                onclick={() => void handleToggleMediaType(root, "scanVideos")}
                title={root.scanVideos ? "Videos: scanning" : "Videos: skipped"}
                class={cn(
                  "flex items-center gap-1.5 rounded-xs px-2 py-1 text-[0.68rem] font-medium border transition-all duration-fast",
                  root.scanVideos
                    ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                    : "bg-surface-1 border-border-subtle text-text-disabled hover:text-text-muted hover:border-border-default",
                )}
              >
                <Film class="h-3.5 w-3.5" />
                Video
              </button>

              <button
                type="button"
                onclick={() => void handleToggleMediaType(root, "scanImages")}
                title={root.scanImages ? "Images: scanning" : "Images: skipped"}
                class={cn(
                  "flex items-center gap-1.5 rounded-xs px-2 py-1 text-[0.68rem] font-medium border transition-all duration-fast",
                  root.scanImages
                    ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                    : "bg-surface-1 border-border-subtle text-text-disabled hover:text-text-muted hover:border-border-default",
                )}
              >
                <ImageIcon class="h-3.5 w-3.5" />
                Image
              </button>

              <button
                type="button"
                onclick={() => void handleToggleMediaType(root, "scanAudio")}
                title={root.scanAudio ? "Audio: scanning" : "Audio: skipped"}
                class={cn(
                  "flex items-center gap-1.5 rounded-xs px-2 py-1 text-[0.68rem] font-medium border transition-all duration-fast",
                  root.scanAudio
                    ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                    : "bg-surface-1 border-border-subtle text-text-disabled hover:text-text-muted hover:border-border-default",
                )}
              >
                <Music class="h-3.5 w-3.5" />
                Audio
              </button>

              <button
                type="button"
                onclick={() => void handleToggleMediaType(root, "scanBooks")}
                title={
                  root.scanBooks && root.scanImages
                    ? "Books and Images: ZIP/CBZ files can appear in both"
                    : root.scanBooks
                      ? "Books: scanning"
                      : "Books: skipped"
                }
                class={cn(
                  "flex items-center gap-1.5 rounded-xs px-2 py-1 text-[0.68rem] font-medium border transition-all duration-fast",
                  root.scanBooks
                    ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                    : "bg-surface-1 border-border-subtle text-text-disabled hover:text-text-muted hover:border-border-default",
                )}
              >
                <BookOpen class="h-3.5 w-3.5" />
                Books
              </button>

              <div class="w-px h-4 bg-border-subtle mx-1 hidden sm:block"></div>

              <button
                type="button"
                onclick={() => void handleToggleNsfw(root)}
                title={root.isNsfw ? "NSFW library: on" : "NSFW library: off"}
                class={cn(
                  "flex items-center gap-1.5 rounded-xs px-2 py-1 text-[0.68rem] font-medium border transition-all duration-fast",
                  root.isNsfw
                    ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                    : "bg-surface-1 border-border-subtle text-text-disabled hover:text-text-muted hover:border-border-default",
                )}
              >
                <Eye class="h-3.5 w-3.5" />
                NSFW
              </button>
            </div>

            <div
              class="text-[0.65rem] text-text-disabled flex items-center gap-1.5 whitespace-nowrap ml-auto"
            >
              <Clock class="h-3 w-3" />
              Last scan: {formatTimestamp(root.lastScannedAt)}
            </div>
          </div>
        </div>
      {/each}
    </div>
  {/if}
  </div>
</Panel>
