<script lang="ts">
  import { X, BookOpen, Film, Images, Layers, Music, ListMusic } from "@lucide/svelte";
  import type { CollectionEntityType } from "$lib/collections/models";
  import { assetUrl } from "$lib/api/orval-fetch";
  import { usePlaylist } from "$lib/stores/playlist.svelte";
  import {
    getEntityMeta,
    getEntityThumbnail,
    getEntityTitle,
  } from "./collections/collection-item-helpers";

  interface Props {
    open: boolean;
    onClose: () => void;
  }

  let { open, onClose }: Props = $props();
  const playlist = usePlaylist();

  const typeIcons: Record<CollectionEntityType, typeof Film> = {
    video: Film,
    gallery: Images,
    book: BookOpen,
    image: Layers,
    "audio-track": Music,
  };

  $effect(() => {
    if (!open) return;
    const currentButton = document.querySelector<HTMLButtonElement>(
      '[data-playlist-current="true"]',
    );
    if (!currentButton) return;
    currentButton.scrollIntoView({ block: "center", behavior: "smooth" });
  });
</script>

<div
  id="playlist-queue-sheet"
  role="region"
  aria-label="Playlist queue"
  aria-hidden={open ? undefined : "true"}
  inert={!open}
  class={`fixed z-[55] transition-transform duration-300 ease-[var(--ease-mechanical)]
    inset-x-0 bottom-14 md:bottom-14 md:left-auto md:right-0 md:w-[420px]
    h-[calc(100dvh-7rem)] md:h-auto md:max-h-[70vh]
    ${open ? "translate-y-0" : "pointer-events-none translate-y-[calc(100%+3.5rem)]"}`}
>
  <div class="flex h-full flex-col border border-border-subtle bg-surface-1/95 shadow-2xl backdrop-blur-xl md:h-auto">
    <div class="flex shrink-0 items-center justify-between border-b border-border-subtle px-4 py-3">
      <div class="flex min-w-0 items-center gap-2">
        <ListMusic class="h-4 w-4 shrink-0 text-text-accent" />
        <a
          href={playlist.collectionId ? `/collections/${playlist.collectionId}` : "/collections"}
          class="truncate text-[0.78rem] font-heading font-medium text-text-primary transition-colors hover:text-text-accent"
          onclick={onClose}
        >
          {playlist.collectionName}
        </a>
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <span class="text-[0.65rem] font-mono text-text-disabled">
          {playlist.orderPosition + 1}/{playlist.orderedItems.length}
        </span>
        <button
          type="button"
          class="p-1 text-text-muted transition-colors hover:text-text-secondary"
          aria-label="Close playlist queue"
          onclick={onClose}
        >
          <X class="h-4 w-4" />
        </button>
      </div>
    </div>

    <div class="flex-1 overflow-y-auto py-1">
      {#each playlist.orderedItems as item, position (item.id)}
        {@const Icon = typeIcons[item.entityType]}
        {@const title = getEntityTitle(item)}
        {@const meta = getEntityMeta(item)}
        {@const thumbnailUrl = assetUrl(getEntityThumbnail(item))}
        {@const isCurrent = position === playlist.orderPosition}
        {@const isPlayed = position < playlist.orderPosition}
        <button
          type="button"
          data-playlist-current={isCurrent ? "true" : "false"}
          class={`w-full flex items-center gap-3 px-4 py-2.5 text-left transition-colors ${
            isCurrent
              ? "border-l-2 border-accent-brass bg-accent-brass/10 shadow-[inset_0_0_12px_rgba(196,154,90,0.08)]"
              : isPlayed
                ? "border-l-2 border-transparent opacity-50"
                : "border-l-2 border-transparent hover:bg-surface-2"
          }`}
          onclick={() => {
            playlist.jumpTo(position);
            onClose();
          }}
        >
          <div class="relative aspect-video w-12 shrink-0 overflow-hidden bg-surface-2">
            {#if thumbnailUrl}
              <img src={thumbnailUrl} alt="" class="h-full w-full object-cover" />
            {:else}
              <div class="flex h-full w-full items-center justify-center">
                <Icon class="h-4 w-4 text-text-disabled" />
              </div>
            {/if}
            <div class="absolute bottom-0 left-0 bg-black/60 px-0.5 py-px text-[0.5rem] font-mono uppercase text-text-secondary">
              <Icon class="inline h-2 w-2" />
            </div>
          </div>

          <div class="min-w-0 flex-1">
            <p
              class={`truncate text-[0.78rem] font-heading font-medium leading-tight ${
                isCurrent
                  ? "text-text-accent"
                  : isPlayed
                    ? "text-text-disabled"
                    : "text-text-primary"
              }`}
            >
              {title}
            </p>
            {#if meta}
              <p class="mt-0.5 truncate text-[0.6rem] font-mono leading-tight text-text-disabled">
                {meta}
              </p>
            {/if}
          </div>

          <span class="shrink-0 text-[0.6rem] font-mono text-text-disabled">
            {position + 1}
          </span>
        </button>
      {/each}
    </div>
  </div>
</div>
