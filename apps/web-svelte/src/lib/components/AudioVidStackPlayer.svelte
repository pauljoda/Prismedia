<script lang="ts">
  import "vidstack/player";
  import "vidstack/player/ui";

  import { onMount } from "svelte";
  import {
    Music,
    Pause,
    Play,
    Repeat,
    Repeat1,
    Shuffle,
    SkipBack,
    SkipForward,
    Volume1,
    Volume2,
    VolumeX,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { formatDuration, type AudioTrackListItemDto } from "@prismedia/contracts";
  import type {
    MediaProviderChangeEvent,
    MediaTimeUpdateEvent,
  } from "vidstack";
  import type { MediaPlayerElement } from "vidstack/elements";
  import { apiAssetUrl } from "$lib/api/orval-fetch";
  import AudioWaveformFilmstrip from "./AudioWaveformFilmstrip.svelte";

  type RepeatMode = "off" | "all" | "one";

  interface Props {
    tracks: AudioTrackListItemDto[];
    activeTrackId: string | null;
    onTrackChange: (trackId: string) => void;
    class?: string;
    libraryCoverUrl?: string;
    shufflePlayKey?: number;
    onPlaybackComplete?: () => void;
    onPlayingChange?: (playing: boolean) => void;
  }

  let {
    tracks,
    activeTrackId,
    onTrackChange,
    class: className = "",
    libraryCoverUrl,
    shufflePlayKey = 0,
    onPlaybackComplete,
    onPlayingChange,
  }: Props = $props();

  let player: MediaPlayerElement | null = $state(null);
  let audioEl: HTMLAudioElement | null = $state(null);
  let playing = $state(false);
  let currentTime = $state(0);
  let duration = $state(0);
  let volume = $state(1);
  let muted = $state(false);
  let waveformData = $state<number[] | null>(null);
  let repeat = $state<RepeatMode>("off");
  let shuffle = $state(false);
  let timelineDragging = $state(false);
  let mediaMounted = $state(false);

  let timelineDraggingRef = false;
  let lastShufflePlayKey = 0;

  const activeTrack = $derived(tracks.find((t) => t.id === activeTrackId) ?? null);
  const activeIndex = $derived(
    activeTrack ? tracks.findIndex((t) => t.id === activeTrackId) : -1,
  );
  const hasNext = $derived(activeIndex >= 0 && activeIndex < tracks.length - 1);
  const hasPrev = $derived(activeIndex > 0);
  const progress = $derived(duration > 0 ? (currentTime / duration) * 100 : 0);

  const playerSrc = $derived.by(() => {
    if (!activeTrack) return undefined;
    return apiAssetUrl(`/audio-stream/${activeTrack.id}`);
  });

  function isKeyboardShortcutSuppressed(target: EventTarget | null): boolean {
    if (!(target instanceof HTMLElement)) return false;
    if (target.isContentEditable) return true;
    return Boolean(target.closest("input, textarea, select"));
  }

  function syncAudioElement() {
    const audio = player?.querySelector("audio") ?? null;
    audioEl = audio;
  }

  function handleSeek(time: number) {
    if (!player) return;
    player.currentTime = time;
    currentTime = time;
  }

  function toggleMute() {
    if (!player) return;
    player.muted = !player.muted;
    muted = player.muted;
  }

  function cycleRepeat() {
    repeat = repeat === "off" ? "all" : repeat === "all" ? "one" : "off";
  }

  function playAll() {
    if (tracks.length === 0) return;
    if (shuffle) {
      const randomTrack = tracks[Math.floor(Math.random() * tracks.length)];
      onTrackChange(randomTrack!.id);
      return;
    }
    onTrackChange(tracks[0]!.id);
  }

  function togglePlay() {
    if (!player) return;
    if (!activeTrack) {
      playAll();
      return;
    }
    if (player.paused) {
      void player.play();
    } else {
      player.pause();
    }
  }

  function handleNext() {
    if (tracks.length === 0) return;
    if (shuffle) {
      const otherTracks = tracks.filter((t) => t.id !== activeTrackId);
      if (otherTracks.length > 0) {
        const randomTrack = otherTracks[Math.floor(Math.random() * otherTracks.length)];
        onTrackChange(randomTrack!.id);
      }
      return;
    }
    if (hasNext) {
      onTrackChange(tracks[activeIndex + 1]!.id);
      return;
    }
    if (repeat === "all") {
      onTrackChange(tracks[0]!.id);
    }
  }

  function handlePrev() {
    if (player && player.currentTime > 3) {
      player.currentTime = 0;
      return;
    }
    if (hasPrev) {
      onTrackChange(tracks[activeIndex - 1]!.id);
    }
  }

  function handleTrackEnd() {
    if (repeat === "one") {
      if (player) {
        player.currentTime = 0;
        void player.play();
      }
      return;
    }

    if (shuffle) {
      const otherTracks = tracks.filter((t) => t.id !== activeTrackId);
      if (otherTracks.length > 0) {
        const randomTrack = otherTracks[Math.floor(Math.random() * otherTracks.length)];
        onTrackChange(randomTrack!.id);
      } else if (repeat === "all" && tracks.length > 0) {
        onTrackChange(tracks[0]!.id);
      }
      return;
    }

    if (activeIndex >= 0 && activeIndex < tracks.length - 1) {
      onTrackChange(tracks[activeIndex + 1]!.id);
      return;
    }

    if (repeat === "all" && tracks.length > 0) {
      onTrackChange(tracks[0]!.id);
      return;
    }

    playing = false;
    onPlaybackComplete?.();
  }

  function handleVolumeInput(event: Event) {
    if (!player) return;
    const nextVolume = Number((event.currentTarget as HTMLInputElement).value);
    player.volume = nextVolume;
    volume = nextVolume;
    if (nextVolume > 0 && player.muted) {
      player.muted = false;
      muted = false;
    }
  }

  function recordTrackPlay(trackId: string) {
    const url = apiAssetUrl(`/audio-tracks/${trackId}/play`);
    if (!url) return;
    void fetch(url, { method: "POST" }).catch(() => {});
  }

  function handleProviderChange(_event: Event) {
    syncAudioElement();
  }

  function handleTimeUpdate(event: Event) {
    const detail = (event as MediaTimeUpdateEvent).detail;
    currentTime = detail.currentTime;
  }

  function handlePlay() {
    playing = true;
    onPlayingChange?.(true);
  }

  function handlePause() {
    playing = false;
    onPlayingChange?.(false);
  }

  function handleCanPlay() {
    syncAudioElement();
    if (player && Number.isFinite(player.duration)) {
      duration = player.duration;
    }
  }

  function handleEnded() {
    if (activeTrackId) recordTrackPlay(activeTrackId);
    handleTrackEnd();
  }

  function handleDurationChange() {
    if (player && Number.isFinite(player.duration)) {
      duration = player.duration;
    }
  }

  // Load waveform data for the active track
  $effect(() => {
    if (!activeTrack?.waveformPath) {
      waveformData = null;
      return;
    }

    const waveformUrl = apiAssetUrl(
      `/assets/${activeTrack.waveformPath.replace(/^\/+/, "")}`,
    );
    if (!waveformUrl) {
      waveformData = null;
      return;
    }

    let cancelled = false;
    fetch(waveformUrl)
      .then((response) => {
        if (!response.ok) throw new Error(`Waveform fetch failed: ${response.status}`);
        return response.json() as Promise<{ data?: number[] }>;
      })
      .then((payload) => {
        if (cancelled) return;
        waveformData = Array.isArray(payload.data) ? payload.data : null;
      })
      .catch(() => {
        if (cancelled) return;
        waveformData = null;
      });

    return () => {
      cancelled = true;
    };
  });

  // Update duration when active track changes
  $effect(() => {
    if (!activeTrack) {
      currentTime = 0;
      duration = 0;
      return;
    }
    currentTime = 0;
    duration = activeTrack.duration ?? 0;
  });

  // Handle shuffle play key
  $effect(() => {
    if (shufflePlayKey === 0 || shufflePlayKey === lastShufflePlayKey || tracks.length === 0) {
      return;
    }
    lastShufflePlayKey = shufflePlayKey;
    shuffle = true;
    const randomTrack = tracks[Math.floor(Math.random() * tracks.length)];
    if (randomTrack?.id === activeTrackId) {
      if (player) void player.play();
    } else if (randomTrack) {
      onTrackChange(randomTrack.id);
    }
  });

  onMount(() => {
    mediaMounted = true;
    return () => {
      mediaMounted = false;
    };
  });

  $effect(() => {
    const el = player;
    if (!el) return;

    const listeners: Array<[string, EventListener]> = [
      ["provider-change", handleProviderChange],
      ["can-play", handleCanPlay],
      ["time-update", handleTimeUpdate],
      ["play", handlePlay],
      ["pause", handlePause],
      ["ended", handleEnded],
      ["duration-change", handleDurationChange],
    ];

    for (const [type, listener] of listeners) el.addEventListener(type, listener);
    return () => {
      for (const [type, listener] of listeners) el.removeEventListener(type, listener);
    };
  });

  function handleKeydown(event: KeyboardEvent) {
    if (isKeyboardShortcutSuppressed(event.target)) return;

    const seekBy = (delta: number) => {
      if (!player || !activeTrack) return;
      const max =
        duration > 0 && Number.isFinite(duration)
          ? duration
          : Number.isFinite(player.duration) && player.duration > 0
            ? player.duration
            : Number.POSITIVE_INFINITY;
      handleSeek(Math.max(0, Math.min(max, (player.currentTime ?? 0) + delta)));
    };

    switch (event.key.toLowerCase()) {
      case " ":
        event.preventDefault();
        togglePlay();
        break;
      case "k":
        if (event.metaKey || event.ctrlKey) break;
        event.preventDefault();
        togglePlay();
        break;
      case "arrowleft":
        seekBy(-5);
        break;
      case "arrowright":
        seekBy(5);
        break;
      case "j":
        seekBy(-10);
        break;
      case "l":
        seekBy(10);
        break;
      case "m":
        toggleMute();
        break;
    }
  }

  const VolumeIcon = $derived(
    muted || volume === 0 ? VolumeX : volume < 0.5 ? Volume1 : Volume2,
  );
</script>

<svelte:window onkeydown={handleKeydown} />

<div class={cn("surface-panel border border-border-subtle", className)}>
  {#if playerSrc && mediaMounted}
    <!-- svelte-ignore a11y_click_events_have_key_events -->
    <media-player
      class="audio-vidstack-engine"
      title={activeTrack?.title ?? "Audio"}
      src={playerSrc}
      viewType="audio"
      streamType="on-demand"
      crossOrigin
      playsInline
      bind:this={player}
    >
      <media-provider></media-provider>
    </media-player>
  {/if}

  <div class="px-4 pt-4 pb-2">
    <div class="mb-3 flex items-center gap-3">
      <div class="relative flex h-10 w-10 shrink-0 items-center justify-center overflow-hidden bg-surface-3 surface-card-sharp">
        <Music class={cn("h-4 w-4", activeTrack ? "text-accent-500" : "text-text-disabled")} />
        {#if libraryCoverUrl}
          <img
            src={libraryCoverUrl}
            alt=""
            class="absolute inset-0 h-full w-full object-cover"
            decoding="async"
            onerror={(event) => ((event.currentTarget as HTMLImageElement).style.display = "none")}
          />
        {/if}
      </div>

      <div class="min-w-0 flex-1">
        {#if activeTrack}
          <p class="truncate text-sm font-medium text-text-primary">{activeTrack.title}</p>
          <p class="truncate text-xs text-text-muted">
            {activeTrack.embeddedArtist ?? "Unknown artist"}
          </p>
        {:else}
          <p class="text-sm text-text-muted">No track playing</p>
          <p class="text-xs text-text-disabled">Select a track or press play</p>
        {/if}
      </div>

      <span class="shrink-0 font-mono tabular-nums text-xs text-text-muted">
        {#if activeTrack}
          {formatDuration(currentTime) ?? "0:00"}
          <span class="text-text-disabled"> / {formatDuration(duration) ?? "0:00"}</span>
        {:else}
          --:--
        {/if}
      </span>
    </div>

    {#if activeTrack && duration > 0}
      <!-- svelte-ignore a11y_no_static_element_interactions -->
      <div
        class="video-progress-track group/track mb-2"
        data-dragging={timelineDragging}
        onpointerdown={(event) => {
          if (duration <= 0) return;
          timelineDraggingRef = true;
          timelineDragging = true;
          (event.currentTarget as HTMLDivElement).setPointerCapture(event.pointerId);
          const rect = (event.currentTarget as HTMLDivElement).getBoundingClientRect();
          const nextPercent = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
          handleSeek(nextPercent * duration);
        }}
        onpointermove={(event) => {
          if (!timelineDraggingRef || duration <= 0) return;
          const rect = (event.currentTarget as HTMLDivElement).getBoundingClientRect();
          const nextPercent = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
          handleSeek(nextPercent * duration);
        }}
        onpointerup={(event) => {
          (event.currentTarget as HTMLDivElement).releasePointerCapture(event.pointerId);
          timelineDraggingRef = false;
          timelineDragging = false;
        }}
        onpointercancel={() => {
          timelineDraggingRef = false;
          timelineDragging = false;
        }}
      >
        <div class="video-progress-fill" style={`width: ${progress}%`}></div>
      </div>
    {/if}
  </div>

  {#if activeTrack && waveformData && duration > 0}
    <div class="overflow-hidden border-t border-border-subtle bg-black">
      <AudioWaveformFilmstrip
        peaks={waveformData}
        {duration}
        {audioEl}
        onSeek={handleSeek}
      />
    </div>
  {/if}

  <div class="grid grid-cols-[1fr_auto_1fr] items-center gap-x-2 px-4 py-2.5">
    <div class="min-w-0" aria-hidden="true"></div>

    <div class="flex items-center justify-center gap-1">
      <button
        type="button"
        onclick={() => (shuffle = !shuffle)}
        title={shuffle ? "Shuffle: on" : "Shuffle: off"}
        class={cn(
          "p-2 transition-colors",
          shuffle ? "text-accent-500" : "text-text-disabled hover:text-text-muted",
        )}
      >
        <Shuffle class="h-3.5 w-3.5" />
      </button>

      <button
        type="button"
        onclick={handlePrev}
        disabled={!activeTrack && tracks.length === 0}
        class="p-2 text-text-muted transition-colors hover:text-text-primary disabled:text-text-disabled"
      >
        <SkipBack class="h-4 w-4" />
      </button>

      <button
        type="button"
        onclick={togglePlay}
        class={cn(
          "mx-1 p-3 transition-all",
          playing
            ? "bg-accent-500 text-bg shadow-[0_0_12px_rgba(196,154,90,0.3)]"
            : "bg-surface-3 text-text-primary hover:bg-surface-4 hover:text-accent-400",
        )}
      >
        {#if playing}
          <Pause class="h-5 w-5" />
        {:else}
          <Play class="ml-0.5 h-5 w-5" />
        {/if}
      </button>

      <button
        type="button"
        onclick={handleNext}
        disabled={!activeTrack && tracks.length === 0}
        class="p-2 text-text-muted transition-colors hover:text-text-primary disabled:text-text-disabled"
      >
        <SkipForward class="h-4 w-4" />
      </button>

      <button
        type="button"
        onclick={cycleRepeat}
        title={repeat === "off" ? "Repeat: off" : repeat === "all" ? "Repeat: all" : "Repeat: one"}
        class={cn(
          "p-2 transition-colors",
          repeat !== "off" ? "text-accent-500" : "text-text-disabled hover:text-text-muted",
        )}
      >
        {#if repeat === "one"}
          <Repeat1 class="h-3.5 w-3.5" />
        {:else}
          <Repeat class="h-3.5 w-3.5" />
        {/if}
      </button>
    </div>

    <div class="group/vol flex min-w-0 items-center justify-end gap-1.5">
      <button
        type="button"
        onclick={toggleMute}
        class="p-1.5 text-text-disabled transition-colors hover:text-text-muted"
      >
        <VolumeIcon class="h-3.5 w-3.5" />
      </button>
      <div class="w-0 overflow-hidden transition-all duration-200 group-hover/vol:w-20">
        <input
          type="range"
          min="0"
          max="1"
          step="0.01"
          value={muted ? 0 : volume}
          oninput={handleVolumeInput}
          class="h-1 w-full cursor-pointer accent-accent-500"
        />
      </div>
    </div>
  </div>
</div>

<style>
  .audio-vidstack-engine {
    position: absolute;
    width: 0;
    height: 0;
    overflow: hidden;
    pointer-events: none;
    opacity: 0;
  }
</style>
