<script lang="ts">
  import {
    Check,
    EllipsisVertical,
    Music2,
    Pencil,
    Play,
    Trash2,
    X,
  } from "@lucide/svelte";
  import { Checkbox, cn } from "@prismedia/ui-svelte";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
  import StarRatingPicker from "./StarRatingPicker.svelte";

  interface Props {
    track: AudioTrackListItemDto;
    artworkUrl?: string | null;
    showArtwork?: boolean;
    index: number;
    isActive: boolean;
    isPlaying: boolean;
    onPlay: (trackId: string) => void;
    onRatingChange?: (trackId: string, value: number | null) => void;
    onRename?: (track: AudioTrackListItemDto, title: string) => void | Promise<void>;
    onDelete?: (track: AudioTrackListItemDto) => void;
    onSelectedChange?: (selected: boolean) => void;
    selectable?: boolean;
    selected?: boolean;
    trackHref?: string;
    ratingAriaPrefix?: string;
    /** Explicit number to show in the "#" column, overriding the index-based default. Used so multi-disc albums restart numbering per section. */
    displayNumber?: number;
  }

  let {
    track,
    artworkUrl = null,
    showArtwork = false,
    index,
    isActive,
    isPlaying,
    onPlay,
    onRatingChange,
    onRename,
    onDelete,
    onSelectedChange,
    selectable = false,
    selected = false,
    trackHref,
    ratingAriaPrefix,
    displayNumber,
  }: Props = $props();

  let menuOpen = $state(false);
  let renaming = $state(false);
  let renameTitle = $state("");
  let renameBusy = $state(false);
  let renameError = $state<string | null>(null);

  const displayTrackNumber = $derived(displayNumber ?? (track.trackNumber ?? index) + 1);
  const presenceKnown = $derived(track.hasSourceMedia !== undefined || track.isWanted !== undefined);
  const isMissing = $derived(track.hasSourceMedia === false || track.isWanted === true);

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

  function handleSelectedChange(event: Event) {
    onSelectedChange?.((event.currentTarget as HTMLInputElement).checked);
  }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<!-- svelte-ignore a11y_click_events_have_key_events -->
<div
  class={cn(
    "track-row group/row relative transition-colors duration-fast",
    "before:pointer-events-none before:absolute before:inset-y-0 before:left-0 before:w-[2px] before:transition-all before:duration-normal",
    selectable && "has-selection",
    showArtwork && "has-artwork",
    isMissing ? "is-missing cursor-default" : "cursor-pointer",
    isActive
      ? "bg-surface-2 before:bg-[var(--entity-accent,var(--color-accent-500))]"
      : "hover:bg-surface-2 before:bg-transparent",
  )}
  onclick={(e) => {
    if (isMissing) return;
    // Don't intercept clicks on interactive children (links, buttons, rating stars)
    const target = e.target as HTMLElement;
    if (target.closest("a, button, input, [role='slider'], [role='menu']")) return;
    onPlay(track.id);
  }}
>
  {#if selectable}
    <div class="selection-cell flex items-center justify-center">
      <Checkbox
        checked={selected}
        disabled={isMissing}
        aria-label={`Select ${track.title}`}
        onchange={handleSelectedChange}
      />
    </div>
  {/if}

  {#if showArtwork}
    <div class="artwork-cell">
      {#if artworkUrl}
        <img src={artworkUrl} alt="" loading="lazy" />
      {:else}
        <span class="artwork-placeholder" aria-hidden="true">
          <Music2 class="h-4 w-4" />
        </span>
      {/if}
    </div>
  {/if}

  <div class="index-cell flex h-7 w-7 items-center justify-center">
    {#if !isMissing && isActive && isPlaying}
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
        isMissing
          ? "text-text-disabled"
          : isActive
            ? "text-accent-400 opacity-0"
            : "text-text-disabled group-hover/row:opacity-0",
      )}>
        {displayTrackNumber}
      </span>
      {#if !isMissing}
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
    {#if !renaming && presenceKnown}
      <p class={cn(
        "track-presence mt-1 font-mono text-[0.62rem] font-semibold uppercase tracking-wider",
        isMissing ? "text-warning-text" : "text-success-text",
      )}>
        {isMissing ? "Missing · not playable" : "Present"}
      </p>
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
    isMissing ? "opacity-50" : onRatingChange ? "opacity-80 hover:opacity-100 focus-within:opacity-100" : "opacity-60",
  )}>
    {#if isMissing}
      <span class="font-mono text-[0.66rem] text-text-disabled">—</span>
    {:else}
      <StarRatingPicker
        value={track.rating}
        onChange={onRatingChange ? (v) => onRatingChange!(track.id, v) : undefined}
        readOnly={!onRatingChange}
        ariaLabelPrefix={ratingAriaPrefix ?? `Rate ${track.title} with`}
      />
    {/if}
  </div>

  <div class="time-cell flex flex-col items-end gap-0.5 font-mono text-[0.72rem] tabular-nums text-text-muted">
    {#if track.playCount > 0}
      <span
        class="inline-flex items-center gap-0.5 text-[0.66rem] text-text-disabled"
        title={track.playCount === 1 ? "Played once" : `Played ${track.playCount} times`}
      >
        <Play size={10} class="fill-current opacity-70" aria-hidden="true" />
        {track.playCount}
      </span>
    {/if}
    <span>{formatDuration(track.duration) ?? "—"}</span>
  </div>

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
        class="floating-surface absolute right-0 top-8 z-20 min-w-36 overflow-hidden py-1"
        use:keepFlyoutOnScreen
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
      "index title  title  actions"
      "index rating time   actions";
    align-items: center;
    column-gap: 0.75rem;
    row-gap: 0.45rem;
    min-height: 4.75rem;
    padding: 0.75rem;
  }

  .track-row.is-missing {
    background: color-mix(in srgb, var(--color-warning, #f59e0b) 3%, transparent);
  }

  .track-row.has-selection {
    grid-template-columns: auto auto minmax(0, 1fr) auto auto;
    grid-template-areas:
      "select index title  title  actions"
      "select index rating time   actions";
  }

  .track-row.has-artwork {
    grid-template-columns: auto 2.75rem minmax(0, 1fr) auto auto;
    grid-template-areas:
      "index artwork title  title  actions"
      "index artwork rating time   actions";
  }

  .track-row.has-selection.has-artwork {
    grid-template-columns: auto auto 2.75rem minmax(0, 1fr) auto auto;
    grid-template-areas:
      "select index artwork title  title  actions"
      "select index artwork rating time   actions";
  }

  .selection-cell { grid-area: select; justify-self: start; align-self: start; padding-top: 0.35rem; }
  .index-cell { grid-area: index; justify-self: start; align-self: start; }
  .artwork-cell { grid-area: artwork; }
  .title-cell { grid-area: title; }
  .rating-cell { grid-area: rating; justify-self: start; }
  .time-cell { grid-area: time; justify-self: end; }
  .actions-cell { grid-area: actions; justify-self: end; }

  .artwork-cell,
  .artwork-cell img,
  .artwork-placeholder {
    width: 2.5rem;
    height: 2.5rem;
  }

  .artwork-cell img,
  .artwork-placeholder {
    display: flex;
    align-items: center;
    justify-content: center;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
  }

  .artwork-cell img {
    object-fit: cover;
  }

  .artwork-placeholder {
    color: var(--color-text-disabled);
  }

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

    .track-row.has-selection {
      grid-template-columns: 1.35rem 2rem minmax(0, 1fr) auto 3rem 2rem;
      grid-template-areas: "select index title rating time actions";
    }

    .track-row.has-artwork {
      grid-template-columns: 2rem 2.75rem minmax(0, 1fr) auto 3rem 2rem;
      grid-template-areas: "index artwork title rating time actions";
      min-height: 3.5rem;
    }

    .track-row.has-selection.has-artwork {
      grid-template-columns: 1.35rem 2rem 2.75rem minmax(0, 1fr) auto 3rem 2rem;
      grid-template-areas: "select index artwork title rating time actions";
    }

    .selection-cell,
    .index-cell,
    .rating-cell,
    .time-cell,
    .actions-cell {
      justify-self: end;
    }

    .index-cell {
      justify-self: center;
      align-self: center;
    }

    .selection-cell {
      justify-self: center;
      align-self: center;
      padding-top: 0;
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
