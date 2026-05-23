<script lang="ts">
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
  import { apiAssetUrl as toApiUrl } from "$lib/api/orval-fetch";
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

  let timelineDraggingRef = false;
  let lastShufflePlayKey = 0;

  const activeTrack = $derived(tracks.find((track) => track.id === activeTrackId) ?? null);
  const activeIndex = $derived(
    activeTrack ? tracks.findIndex((track) => track.id === activeTrackId) : -1,
  );
  const hasNext = $derived(activeIndex >= 0 && activeIndex < tracks.length - 1);
  const hasPrev = $derived(activeIndex > 0);
  const progress = $derived(duration > 0 ? (currentTime / duration) * 100 : 0);

  function isKeyboardShortcutSuppressed(target: EventTarget | null): boolean {
    if (!(target instanceof HTMLElement)) return false;
    if (target.isContentEditable) return true;
    return Boolean(target.closest("input, textarea, select"));
  }

  function requestPlay() {
    if (!audioEl) return;
    const playPromise = audioEl.play();
    if (playPromise && typeof playPromise.catch === "function") {
      void playPromise.catch((error: unknown) => {
        console.error("Audio play failed:", error);
      });
    }
  }

  function handleSeek(time: number) {
    if (!audioEl) return;
    audioEl.currentTime = time;
    currentTime = time;
  }

  function toggleMute() {
    if (!audioEl) return;
    audioEl.muted = !audioEl.muted;
    muted = audioEl.muted;
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
    if (!audioEl) return;
    if (!activeTrack) {
      playAll();
      return;
    }
    if (audioEl.paused) requestPlay();
    else audioEl.pause();
  }

  function handleNext() {
    if (tracks.length === 0) return;
    if (shuffle) {
      const otherTracks = tracks.filter((track) => track.id !== activeTrackId);
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
    if (audioEl && audioEl.currentTime > 3) {
      audioEl.currentTime = 0;
      return;
    }
    if (hasPrev) {
      onTrackChange(tracks[activeIndex - 1]!.id);
    }
  }

  function handleTrackEnd() {
    if (repeat === "one") {
      if (audioEl) {
        audioEl.currentTime = 0;
        requestPlay();
      }
      return;
    }

    if (shuffle) {
      const otherTracks = tracks.filter((track) => track.id !== activeTrackId);
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
    if (!audioEl) return;
    const nextVolume = Number((event.currentTarget as HTMLInputElement).value);
    audioEl.volume = nextVolume;
    volume = nextVolume;
    if (nextVolume > 0 && audioEl.muted) {
      audioEl.muted = false;
      muted = false;
    }
  }

  function recordTrackPlay(trackId: string) {
    const url = toApiUrl(`/audio-tracks/${trackId}/play`);
    if (!url) return;
    void fetch(url, { method: "POST" }).catch(() => {});
  }

  $effect(() => {
    if (!audioEl) return;
    if (!activeTrack) {
      audioEl.removeAttribute("src");
      audioEl.load();
      playing = false;
      currentTime = 0;
      return;
    }

    const nextSrc = toApiUrl(`/audio-stream/${activeTrack.id}`);
    if (!nextSrc || audioEl.src === nextSrc) return;
    audioEl.src = nextSrc;
    currentTime = 0;
    duration = activeTrack.duration ?? 0;
    requestPlay();
  });

  $effect(() => {
    if (!activeTrack?.waveformPath) {
      waveformData = null;
      return;
    }

    const waveformUrl = toApiUrl(
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
      .catch((error) => {
        if (cancelled) return;
        console.error("Failed to load waveform:", error);
        waveformData = null;
      });

    return () => {
      cancelled = true;
    };
  });

  $effect(() => {
    if (shufflePlayKey === 0 || shufflePlayKey === lastShufflePlayKey || tracks.length === 0) {
      return;
    }
    lastShufflePlayKey = shufflePlayKey;
    shuffle = true;
    const randomTrack = tracks[Math.floor(Math.random() * tracks.length)];
    if (randomTrack?.id === activeTrackId) {
      requestPlay();
    } else if (randomTrack) {
      onTrackChange(randomTrack.id);
    }
  });

  onMount(() => {
    if (!audioEl) return;

    const handleTimeUpdate = () => {
      currentTime = audioEl?.currentTime ?? 0;
    };
    const handleLoadedMetadata = () => {
      if (audioEl && Number.isFinite(audioEl.duration)) duration = audioEl.duration;
    };
    const handleDurationChange = () => {
      if (audioEl && Number.isFinite(audioEl.duration)) duration = audioEl.duration;
    };
    const handlePlay = () => {
      playing = true;
      onPlayingChange?.(true);
    };
    const handlePause = () => {
      playing = false;
      onPlayingChange?.(false);
    };
    const handleEnded = () => {
      if (activeTrackId) recordTrackPlay(activeTrackId);
      handleTrackEnd();
    };
    const handleError = () => {
      console.error("Audio element error:", audioEl?.error);
      playing = false;
      onPlayingChange?.(false);
    };

    audioEl.addEventListener("timeupdate", handleTimeUpdate);
    audioEl.addEventListener("loadedmetadata", handleLoadedMetadata);
    audioEl.addEventListener("durationchange", handleDurationChange);
    audioEl.addEventListener("play", handlePlay);
    audioEl.addEventListener("pause", handlePause);
    audioEl.addEventListener("ended", handleEnded);
    audioEl.addEventListener("error", handleError);

    return () => {
      audioEl?.removeEventListener("timeupdate", handleTimeUpdate);
      audioEl?.removeEventListener("loadedmetadata", handleLoadedMetadata);
      audioEl?.removeEventListener("durationchange", handleDurationChange);
      audioEl?.removeEventListener("play", handlePlay);
      audioEl?.removeEventListener("pause", handlePause);
      audioEl?.removeEventListener("ended", handleEnded);
      audioEl?.removeEventListener("error", handleError);
    };
  });

  function handleKeydown(event: KeyboardEvent) {
    if (isKeyboardShortcutSuppressed(event.target)) return;

    const seekBy = (delta: number) => {
      if (!audioEl || !activeTrack) return;
      const max =
        duration > 0 && Number.isFinite(duration)
          ? duration
          : Number.isFinite(audioEl.duration) && audioEl.duration > 0
            ? audioEl.duration
            : Number.POSITIVE_INFINITY;
      handleSeek(Math.max(0, Math.min(max, audioEl.currentTime + delta)));
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
  <audio bind:this={audioEl} preload="auto"></audio>

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
