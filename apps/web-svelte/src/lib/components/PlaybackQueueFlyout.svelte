<script lang="ts">
  import { ListMusic, Music, Repeat, Repeat1, Shuffle, X } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import { resolveAudioArtwork, useAudioPlayback } from "$lib/stores/audio-playback.svelte";

  interface Props {
    onClose: () => void;
  }

  let { onClose }: Props = $props();

  const playback = useAudioPlayback()!;
  let root: HTMLElement | null = $state(null);

  const current = $derived(playback.currentTrack);
  const upNext = $derived(playback.upNext);
  const cover = $derived(resolveAudioArtwork(current, playback.context));

  function onWindowPointerDown(event: PointerEvent) {
    // Guard against the toggle button: it lives in the flyout's anchor wrapper, so checking the
    // anchor (not just the flyout) lets the trigger's own click close the flyout instead of this
    // handler closing it first and the trigger immediately reopening it.
    const anchor = root?.parentElement ?? root;
    if (anchor && !anchor.contains(event.target as Node)) onClose();
  }
</script>

<svelte:window onpointerdown={onWindowPointerDown} />

<div
  bind:this={root}
  class="absolute bottom-full right-0 z-30 mb-2 max-h-[70vh] w-80 max-w-[calc(100vw-1.5rem)] overflow-hidden rounded-lg border border-border-subtle bg-surface-1 shadow-[0_16px_40px_rgba(0,0,0,0.5)]"
  use:keepFlyoutOnScreen
>
  <div class="flex items-center justify-between border-b border-border-subtle px-3 py-2">
    <div class="flex items-center gap-1.5 text-text-secondary">
      <ListMusic class="h-3.5 w-3.5 text-text-accent" />
      <span class="text-[0.74rem] font-medium">Queue</span>
      {#if playback.shuffle}
        <Shuffle class="h-3 w-3 text-accent-500" />
      {/if}
      {#if playback.repeat === "all"}
        <Repeat class="h-3 w-3 text-accent-500" />
      {:else if playback.repeat === "one"}
        <Repeat1 class="h-3 w-3 text-accent-500" />
      {/if}
    </div>
    <button type="button" onclick={onClose} class="p-0.5 text-text-disabled transition-colors hover:text-text-muted" aria-label="Close queue">
      <X class="h-3.5 w-3.5" />
    </button>
  </div>

  <div class="max-h-[calc(70vh-2.5rem)] overflow-y-auto">
    {#if current}
      <div class="sticky top-0 z-10 bg-surface-1 px-3 pt-2 pb-1.5 shadow-[0_6px_8px_-6px_rgba(0,0,0,0.6)]">
        <p class="text-kicker">Now playing</p>
        <div class="mt-1 flex items-center gap-2.5 rounded-sm bg-surface-2 px-2 py-1.5">
          <div class="h-8 w-8 shrink-0 overflow-hidden rounded">
            {#if cover}
              <img src={cover} alt="" class="h-full w-full object-cover" />
            {:else}
              <div class="flex h-full w-full items-center justify-center bg-black/20 text-accent-500/80"><Music class="h-3.5 w-3.5" /></div>
            {/if}
          </div>
          <div class="min-w-0 flex-1">
            <p class="truncate text-[0.76rem] font-medium text-text-primary">{current.title}</p>
            <p class="truncate text-[0.66rem] text-text-muted">
              {playback.context?.artistName ?? current.embeddedArtist ?? "Unknown artist"}
            </p>
          </div>
        </div>
      </div>
    {/if}

    {#if upNext.length > 0}
      <div class="px-3 pb-2 pt-2.5">
        <p class="text-kicker">Next up · {upNext.length}</p>
        <ul class="mt-1 flex flex-col">
          {#each upNext as track, i (track.id + ":" + i)}
            <li>
              <button
                type="button"
                onclick={() => { playback.jumpTo(playback.position + 1 + i); }}
                class="group flex w-full items-center gap-2.5 rounded-sm px-2 py-1.5 text-left transition-colors hover:bg-surface-2"
              >
                <span class="w-5 shrink-0 text-right font-mono text-[0.64rem] text-text-disabled group-hover:hidden">
                  {(track.trackNumber ?? track.sortOrder + 1)}
                </span>
                <span class="hidden w-5 shrink-0 justify-end group-hover:flex"><Music class="h-3 w-3 text-text-accent" /></span>
                <span class="min-w-0 flex-1">
                  <span class="block truncate text-[0.74rem] text-text-secondary group-hover:text-text-primary">{track.title}</span>
                  {#if track.embeddedArtist}
                    <span class="block truncate text-[0.62rem] text-text-disabled">{track.embeddedArtist}</span>
                  {/if}
                </span>
              </button>
            </li>
          {/each}
        </ul>
      </div>
    {:else}
      <p class="px-3 py-4 text-center text-[0.72rem] text-text-disabled">Nothing up next.</p>
    {/if}
  </div>
</div>
