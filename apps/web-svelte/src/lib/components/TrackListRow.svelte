<script lang="ts">
  import {
    Check,
    EllipsisVertical,
    Pencil,
    Play,
    Trash2,
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { AudioTrackListItemDto } from "@prismedia/contracts";
  import StarRatingPicker from "./StarRatingPicker.svelte";

  interface Props {
    track: AudioTrackListItemDto;
    index: number;
    isActive: boolean;
    isPlaying: boolean;
    onPlay: (trackId: string) => void;
    onRatingChange?: (trackId: string, value: number | null) => void;
    onRename?: (track: AudioTrackListItemDto, title: string) => void | Promise<void>;
    onDelete?: (track: AudioTrackListItemDto) => void;
    trackHref?: string;
    ratingAriaPrefix?: string;
  }

  let {
    track,
    index,
    isActive,
    isPlaying,
    onPlay,
    onRatingChange,
    onRename,
    onDelete,
    trackHref,
    ratingAriaPrefix,
  }: Props = $props();

  let menuOpen = $state(false);
  let renaming = $state(false);
  let renameTitle = $state("");
  let renameBusy = $state(false);
  let renameError = $state<string | null>(null);

  const displayTrackNumber = $derived((track.trackNumber ?? index) + 1);

  function formatDuration(sec: number | null | undefined) {
    if (!sec) return null;
    const total = Math.floor(sec);
    const m = Math.floor(total / 60);
    const s = total % 60;
    const h = Math.floor(m / 60);
    if (h > 0) {
      return `${h}:${String(m % 60).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
    }
    return `${m}:${String(s).padStart(2, "0")}`;
  }

  function beginRename() {
    menuOpen = false;
    renaming = true;
    renameTitle = track.title;
    renameError = null;
  }

  function cancelRename() {
    renaming = false;
    renameTitle = track.title;
    renameError = null;
  }

  async function saveRename() {
    const title = renameTitle.trim();
    if (!title || !onRename) return;
    if (title === track.title) {
      cancelRename();
      return;
    }

    renameBusy = true;
    renameError = null;
    try {
      await onRename(track, title);
      renaming = false;
    } catch (err) {
      renameError = err instanceof Error ? err.message : String(err);
    } finally {
      renameBusy = false;
    }
  }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<!-- svelte-ignore a11y_click_events_have_key_events -->
<div
  class={cn(
    "track-row group/row relative cursor-pointer transition-colors duration-fast",
    "before:pointer-events-none before:absolute before:inset-y-0 before:left-0 before:w-[2px] before:transition-all before:duration-normal",
    isActive
      ? "bg-gradient-to-r from-accent-900/40 via-accent-950/30 to-transparent before:bg-[var(--color-accent-500)] before:shadow-[0_0_12px_rgba(199,155,92,0.55)]"
      : "hover:bg-surface-2 before:bg-transparent",
  )}
  onclick={(e) => {
    // Don't intercept clicks on interactive children (links, buttons, rating stars)
    const target = e.target as HTMLElement;
    if (target.closest("a, button, input, [role='slider'], [role='menu']")) return;
    onPlay(track.id);
  }}
>
  <div class="index-cell flex h-7 w-7 items-center justify-center">
    {#if isActive && isPlaying}
      <span
        class="flex h-4 items-end gap-[2px]"
        aria-label="Now playing"
      >
        <span class="w-[2px] bg-accent-400 animate-[bar-bounce_0.9s_ease-in-out_infinite]" style="height:55%; animation-delay:0ms"></span>
        <span class="w-[2px] bg-accent-400 animate-[bar-bounce_0.9s_ease-in-out_infinite]" style="height:100%; animation-delay:150ms"></span>
        <span class="w-[2px] bg-accent-400 animate-[bar-bounce_0.9s_ease-in-out_infinite]" style="height:40%; animation-delay:300ms"></span>
        <span class="w-[2px] bg-accent-400 animate-[bar-bounce_0.9s_ease-in-out_infinite]" style="height:75%; animation-delay:450ms"></span>
      </span>
    {:else}
      <span class={cn(
        "absolute font-mono text-[0.72rem] tabular-nums transition-opacity duration-fast",
        isActive ? "text-accent-400 opacity-0" : "text-text-disabled group-hover/row:opacity-0",
      )}>
        {displayTrackNumber}
      </span>
      <button
        type="button"
        onclick={() => onPlay(track.id)}
        aria-label={isActive ? "Resume" : `Play ${track.title}`}
        class={cn(
          "inline-flex h-7 w-7 items-center justify-center transition-opacity duration-fast",
          isActive ? "text-accent-400 opacity-100 hover:text-accent-300" : "text-text-primary opacity-0 group-hover/row:opacity-100 hover:text-accent-300",
        )}
      >
        <Play class="h-3.5 w-3.5" fill="currentColor" />
      </button>
    {/if}
  </div>

  <div class="title-cell min-w-0">
    {#if renaming}
      <div class="flex min-w-0 items-center gap-1.5">
        <input
          type="text"
          aria-label="Track title"
          class="min-w-0 flex-1 rounded-xs border border-border-accent bg-surface-1 px-2 py-1 text-[0.82rem] font-medium text-text-primary outline-none shadow-[inset_0_1px_8px_rgba(0,0,0,0.28)]"
          bind:value={renameTitle}
          disabled={renameBusy}
          onkeydown={(event) => {
            if (event.key === "Enter") void saveRename();
            if (event.key === "Escape") cancelRename();
          }}
        />
        <button
          type="button"
          class="inline-flex h-7 w-7 items-center justify-center rounded-xs border border-border-accent bg-accent-950/30 text-text-accent transition-colors hover:bg-accent-950/50 disabled:opacity-40"
          disabled={renameBusy || !renameTitle.trim()}
          aria-label="Save track title"
          onclick={() => void saveRename()}
        >
          <Check class="h-3.5 w-3.5" />
        </button>
        <button
          type="button"
          class="inline-flex h-7 w-7 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3"
          disabled={renameBusy}
          aria-label="Cancel track rename"
          onclick={cancelRename}
        >
          <X class="h-3.5 w-3.5" />
        </button>
      </div>
      {#if renameError}
        <p class="mt-0.5 truncate text-[0.68rem] text-error-text">{renameError}</p>
      {/if}
    {:else if trackHref}
      <a
        href={trackHref}
        class={cn(
          "track-title block text-[0.86rem] font-medium leading-tight transition-colors",
          isActive
            ? "text-text-accent hover:text-accent-200"
            : "text-text-primary hover:text-text-accent",
        )}
      >
        {track.title}
      </a>
    {:else}
      <span
        class={cn(
          "track-title block text-[0.86rem] font-medium leading-tight",
          isActive ? "text-text-accent" : "text-text-primary",
        )}
      >
        {track.title}
      </span>
    {/if}
    {#if !renaming && (track.embeddedArtist || track.embeddedAlbum)}
      <p class="track-subtitle mt-0.5 text-[0.72rem] text-text-muted">
        {track.embeddedArtist ?? ""}
        {#if track.embeddedArtist && track.embeddedAlbum} <span class="text-text-disabled">·</span> {/if}
        {#if track.embeddedAlbum}<span class="text-text-disabled">{track.embeddedAlbum}</span>{/if}
      </p>
    {/if}
  </div>

  <div class={cn(
    "rating-cell transition-opacity duration-fast",
    onRatingChange ? "opacity-80 hover:opacity-100 focus-within:opacity-100" : "opacity-60",
  )}>
    <StarRatingPicker
      value={track.rating}
      onChange={onRatingChange ? (v) => onRatingChange!(track.id, v) : undefined}
      readOnly={!onRatingChange}
      ariaLabelPrefix={ratingAriaPrefix ?? `Rate ${track.title} with`}
    />
  </div>

  <span class="time-cell font-mono text-[0.72rem] tabular-nums text-text-muted">
    {formatDuration(track.duration) ?? "—"}
  </span>

  <div class="actions-cell relative">
    {#if onRename || onDelete}
      <button
        type="button"
        onclick={() => (menuOpen = !menuOpen)}
        aria-label={`Track actions for ${track.title}`}
        aria-haspopup="menu"
        aria-expanded={menuOpen}
        class={cn(
          "inline-flex h-8 w-8 items-center justify-center rounded-xs border border-transparent text-text-disabled transition-all duration-fast hover:border-border-default hover:bg-surface-2 hover:text-text-primary",
          menuOpen ? "border-border-accent bg-accent-950/20 text-text-accent opacity-100" : "opacity-70 hover:opacity-100 focus-visible:opacity-100",
        )}
      >
        <EllipsisVertical class="h-4 w-4" />
      </button>
    {/if}

    {#if menuOpen}
      <div
        role="menu"
        class="absolute right-0 top-8 z-20 min-w-36 overflow-hidden rounded-xs border border-border-default bg-surface-1 py-1 shadow-[0_12px_30px_rgba(0,0,0,0.45)]"
      >
        {#if onRename}
          <button
            type="button"
            role="menuitem"
            class="flex w-full items-center gap-2 px-2.5 py-1.5 text-left text-[0.76rem] text-text-secondary transition-colors hover:bg-surface-2 hover:text-text-primary"
            onclick={beginRename}
          >
            <Pencil class="h-3.5 w-3.5 text-text-accent" />
            Rename
          </button>
        {/if}
        {#if onDelete}
          <button
            type="button"
            role="menuitem"
            class="flex w-full items-center gap-2 px-2.5 py-1.5 text-left text-[0.76rem] text-text-secondary transition-colors hover:bg-surface-2 hover:text-error-text"
            onclick={() => {
              menuOpen = false;
              onDelete?.(track);
            }}
          >
            <Trash2 class="h-3.5 w-3.5" />
            Delete
          </button>
        {/if}
      </div>
    {/if}

    {#if onDelete}
      <button
        type="button"
        onclick={() => onDelete!(track)}
        aria-label={`Delete ${track.title}`}
        class="hidden"
      >
        <Trash2 class="h-3.5 w-3.5" />
      </button>
    {/if}
  </div>
</div>

<style>
  .track-row {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr) auto auto;
    grid-template-areas:
      "title title title title"
      "index rating time actions";
    align-items: center;
    column-gap: 0.75rem;
    row-gap: 0.45rem;
    min-height: 4.75rem;
    padding: 0.75rem;
  }

  .index-cell { grid-area: index; justify-self: start; }
  .title-cell { grid-area: title; }
  .rating-cell { grid-area: rating; justify-self: start; }
  .time-cell { grid-area: time; justify-self: end; }
  .actions-cell { grid-area: actions; justify-self: end; }

  .track-title {
    white-space: normal;
    overflow-wrap: anywhere;
    display: -webkit-box;
    line-clamp: 2;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
  }

  .track-subtitle {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  @media (min-width: 640px) {
    .track-row {
      grid-template-columns: 2rem minmax(0, 1fr) auto 3rem 2rem;
      grid-template-areas: "index title rating time actions";
      min-height: 2.75rem;
      padding: 0.375rem 1rem;
      row-gap: 0;
    }

    .index-cell,
    .rating-cell,
    .time-cell,
    .actions-cell {
      justify-self: end;
    }

    .index-cell {
      justify-self: center;
    }

    .track-title,
    .track-subtitle {
      display: block;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
  }
</style>
