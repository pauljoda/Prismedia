<script lang="ts">
  import { Play, Trash2 } from "@lucide/svelte";
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
    onDelete,
    trackHref,
    ratingAriaPrefix,
  }: Props = $props();

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
</script>

<div
  class={cn(
    "group/row relative grid h-11 grid-cols-[2rem_minmax(0,1fr)_auto_3rem_1.75rem] items-center gap-3 px-3 sm:px-4 transition-colors duration-fast",
    "before:pointer-events-none before:absolute before:inset-y-0 before:left-0 before:w-[2px] before:transition-all before:duration-normal",
    isActive
      ? "bg-gradient-to-r from-accent-900/40 via-accent-950/30 to-transparent before:bg-[var(--color-accent-500)] before:shadow-[0_0_12px_rgba(199,155,92,0.55)]"
      : "hover:bg-surface-2 before:bg-transparent",
  )}
>
  <div class="flex h-7 w-7 items-center justify-center">
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
        {track.trackNumber ?? index + 1}
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

  <div class="min-w-0">
    {#if trackHref}
      <a
        href={trackHref}
        class={cn(
          "block truncate text-[0.86rem] font-medium leading-tight transition-colors",
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
          "block truncate text-[0.86rem] font-medium leading-tight",
          isActive ? "text-text-accent" : "text-text-primary",
        )}
      >
        {track.title}
      </span>
    {/if}
    {#if track.embeddedArtist || track.embeddedAlbum}
      <p class="mt-0.5 truncate text-[0.72rem] text-text-muted">
        {track.embeddedArtist ?? ""}
        {#if track.embeddedArtist && track.embeddedAlbum} <span class="text-text-disabled">·</span> {/if}
        {#if track.embeddedAlbum}<span class="text-text-disabled">{track.embeddedAlbum}</span>{/if}
      </p>
    {/if}
  </div>

  <div class={cn(
    "justify-self-end transition-opacity duration-fast",
    track.rating != null ? "opacity-70 group-hover/row:opacity-100" : "opacity-0 group-hover/row:opacity-100",
  )}>
    <StarRatingPicker
      value={track.rating}
      onChange={onRatingChange ? (v) => onRatingChange!(track.id, v) : undefined}
      readOnly={!onRatingChange}
      ariaLabelPrefix={ratingAriaPrefix ?? `Rate ${track.title} with`}
    />
  </div>

  <span class="justify-self-end font-mono text-[0.72rem] tabular-nums text-text-muted">
    {formatDuration(track.duration) ?? "—"}
  </span>

  <div class="justify-self-end">
    {#if onDelete}
      <button
        type="button"
        onclick={() => onDelete!(track)}
        aria-label={`Delete ${track.title}`}
        class="inline-flex h-8 w-8 items-center justify-center text-text-disabled opacity-0 transition-opacity duration-fast hover:text-error-text group-hover/row:opacity-100"
      >
        <Trash2 class="h-3.5 w-3.5" />
      </button>
    {/if}
  </div>
</div>
