<script lang="ts">
  import { BookOpen, ChevronDown, ChevronUp, Film, Images, Layers, Music, Repeat, Shuffle, SkipBack, SkipForward, X } from "@lucide/svelte";
  import type { CollectionEntityType } from "$lib/collections/models";
  import { usePlaylist } from "$lib/stores/playlist.svelte";
  import { getEntityHref, getEntityTitle } from "./collections/collection-item-helpers";
  import PlaylistQueueSheet from "./PlaylistQueueSheet.svelte";

  const playlist = usePlaylist();

  const typeIcons: Record<CollectionEntityType, typeof Film> = {
    video: Film,
    gallery: Images,
    book: BookOpen,
    image: Layers,
    "audio-track": Music,
  };

  let showQueue = $state(false);
  const queueSheetId = "playlist-queue-sheet";

  const currentTitle = $derived(playlist.currentItem ? getEntityTitle(playlist.currentItem) : "Untitled");
  const CurrentTypeIcon = $derived(
    playlist.currentItem ? typeIcons[playlist.currentItem.entityType] : Film,
  );
</script>

{#if playlist.isActive}
  <PlaylistQueueSheet open={showQueue} onClose={() => (showQueue = false)} />

  <div class="fixed bottom-14 left-0 right-0 z-[55] flex h-14 items-center gap-3 border-t border-border-subtle bg-surface-1/95 px-4 backdrop-blur-xl md:bottom-0">
    <div class="flex min-w-0 flex-1 items-center gap-2">
      <CurrentTypeIcon class="h-4 w-4 shrink-0 text-text-accent" />
      <div class="min-w-0">
        <a
          href={playlist.currentItem ? getEntityHref(playlist.currentItem) : "#"}
          class="block truncate text-[0.78rem] font-medium leading-tight text-text-primary transition-colors hover:text-text-accent"
        >
          {currentTitle}
        </a>
        <a
          href={playlist.collectionId ? `/collections/${playlist.collectionId}` : "/collections"}
          class="block truncate text-[0.65rem] leading-tight text-text-muted transition-colors hover:text-text-accent"
        >
          {playlist.collectionName} — {playlist.orderPosition + 1}/{playlist.items.length}
        </a>
      </div>
    </div>

    <div class="flex items-center gap-1">
      <button
        type="button"
        class={`p-1.5 transition-colors ${playlist.shuffle ? "text-text-accent" : "text-text-muted hover:text-text-secondary"}`}
        aria-label={playlist.shuffle ? "Disable shuffle" : "Enable shuffle"}
        aria-pressed={playlist.shuffle}
        title="Shuffle"
        onclick={() => playlist.toggleShuffle()}
      >
        <Shuffle class="h-3.5 w-3.5" />
      </button>

      <button
        type="button"
        class="p-1.5 text-text-secondary transition-colors hover:text-text-primary"
        aria-label="Previous item"
        title="Previous"
        onclick={() => playlist.previous()}
      >
        <SkipBack class="h-4 w-4" />
      </button>

      <button
        type="button"
        class="p-1.5 text-text-secondary transition-colors hover:text-text-primary"
        aria-label="Next item"
        title="Next"
        onclick={() => playlist.next()}
      >
        <SkipForward class="h-4 w-4" />
      </button>

      <button
        type="button"
        class={`p-1.5 transition-colors ${playlist.loop ? "text-text-accent" : "text-text-muted hover:text-text-secondary"}`}
        aria-label={playlist.loop ? "Disable loop" : "Enable loop"}
        aria-pressed={playlist.loop}
        title="Loop"
        onclick={() => playlist.toggleLoop()}
      >
        <Repeat class="h-3.5 w-3.5" />
      </button>
    </div>

    <div class="flex items-center gap-1">
      <button
        type="button"
        class="p-1.5 text-text-muted transition-colors hover:text-text-secondary"
        aria-controls={queueSheetId}
        aria-expanded={showQueue}
        aria-label={showQueue ? "Hide queue" : "Show queue"}
        title="Queue"
        onclick={() => (showQueue = !showQueue)}
      >
        {#if showQueue}
          <ChevronDown class="h-4 w-4" />
        {:else}
          <ChevronUp class="h-4 w-4" />
        {/if}
      </button>
      <button
        type="button"
        class="p-1.5 text-text-muted transition-colors hover:text-error-text"
        aria-label="End playlist"
        title="End playlist"
        onclick={() => playlist.clearPlaylist()}
      >
        <X class="h-4 w-4" />
      </button>
    </div>
  </div>
{/if}
