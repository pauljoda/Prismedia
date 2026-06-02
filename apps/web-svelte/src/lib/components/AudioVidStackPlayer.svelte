<script lang="ts">
  import { onMount } from "svelte";
  import {
    ListMusic,
    Minimize2,
    Music,
    Music2,
    Music4,
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
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { formatDuration } from "@prismedia/contracts";
  import { apiAssetUrl, assetUrl } from "$lib/api/orval-fetch";
  import { resolveEntityHref } from "$lib/entities/entity-codes";
  import AudioWaveformFilmstrip from "./AudioWaveformFilmstrip.svelte";
  import PlaybackQueueFlyout from "./PlaybackQueueFlyout.svelte";
  import { waveformForDisplay } from "./audio-waveform";
  import { useAudioPlayback } from "$lib/stores/audio-playback.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  const playback = useAudioPlayback()!;
  const chrome = useAppChrome();

  let audioEl: HTMLAudioElement | null = $state(null);
  let rootEl: HTMLElement | null = $state(null);
  let volume = $state(1);
  let muted = $state(false);
  let waveformData = $state<number[] | null>(null);
  let timelineDragging = $state(false);
  let queueOpen = $state(false);
  let collapsed = $state(false);

  let timelineDraggingRef = false;
  let currentSrcTrackId: string | null = null;

  const activeTrack = $derived(playback.currentTrack);
  const ctx = $derived(playback.context);
  const currentTime = $derived(playback.currentTime);
  const duration = $derived(playback.duration);
  const playing = $derived(playback.playing);
  const progress = $derived(
    duration > 0 ? Math.max(0, Math.min(100, (currentTime / duration) * 100)) : 0,
  );

  const artistName = $derived(
    ctx?.artistName ?? activeTrack?.embeddedArtist ?? activeTrack?.performers?.[0]?.name ?? null,
  );
  const artistHref = $derived(ctx?.artistId ? resolveEntityHref("music-artist", ctx.artistId) : undefined);
  const coverUrl = $derived(ctx?.coverUrl ?? null);
  // Album label: a single-album context wins; otherwise fall back to the track's own album
  // so mixed-album queues (e.g. an artist Play All) still show the right album per track.
  const albumLabel = $derived(ctx?.albumTitle ?? activeTrack?.embeddedAlbum ?? null);

  function collapse() {
    collapsed = true;
    queueOpen = false;
  }

  function dismiss() {
    if (audioEl) audioEl.pause();
    playback.clear();
  }

  function isKeyboardShortcutSuppressed(target: EventTarget | null): boolean {
    if (!(target instanceof HTMLElement)) return false;
    if (target.isContentEditable) return true;
    return Boolean(target.closest("input, textarea, select"));
  }

  function requestPlay() {
    if (!audioEl) return;
    const playPromise = audioEl.play();
    if (playPromise && typeof playPromise.catch === "function") {
      void playPromise.catch((error: unknown) => console.error("Audio play failed:", error));
    }
  }

  function resetPlaybackPosition(nextDuration = 0) {
    if (audioEl) {
      try {
        audioEl.currentTime = 0;
      } catch (error) {
        console.warn("Failed to reset audio position:", error);
      }
    }
    playback.currentTime = 0;
    playback.duration = nextDuration;
  }

  function handleSeek(time: number) {
    if (!audioEl) return;
    audioEl.currentTime = time;
    playback.currentTime = time;
  }

  function toggleMute() {
    if (!audioEl) return;
    audioEl.muted = !audioEl.muted;
    muted = audioEl.muted;
  }

  function togglePlay() {
    if (!audioEl) return;
    if (audioEl.paused) requestPlay();
    else audioEl.pause();
  }

  function handleNext() {
    // The Next button advances even in repeat-one; the play position effect loads the new track.
    if (playback.next()) {
      resetPlaybackPosition(playback.currentTrack?.duration ?? 0);
    }
  }

  function handlePrev() {
    if (audioEl && audioEl.currentTime > 3) {
      resetPlaybackPosition(duration);
      return;
    }
    if (playback.prev()) {
      resetPlaybackPosition(playback.currentTrack?.duration ?? 0);
    }
  }

  function handleTrackEnd() {
    if (playback.repeat === "one") {
      resetPlaybackPosition(duration);
      requestPlay();
      return;
    }
    if (playback.next()) {
      resetPlaybackPosition(playback.currentTrack?.duration ?? 0);
      return;
    }
    playback.playing = false;
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
    const url = apiAssetUrl(`/audio-tracks/${trackId}/play`);
    if (!url) return;
    void fetch(url, { method: "POST" }).catch(() => {});
  }

  // Switch audio source when the current track changes.
  $effect(() => {
    if (!audioEl) return;
    const track = activeTrack;
    if (!track) {
      currentSrcTrackId = null;
      audioEl.removeAttribute("src");
      audioEl.load();
      playback.playing = false;
      playback.currentTime = 0;
      playback.duration = 0;
      return;
    }

    if (track.id === currentSrcTrackId) return;
    currentSrcTrackId = track.id;

    const nextSrc = apiAssetUrl(`/audio-stream/${track.id}`);
    if (!nextSrc) return;

    audioEl.src = nextSrc;
    resetPlaybackPosition(track.duration ?? 0);
    audioEl.load();
    // play() is queued until data arrives; the user gesture that changed the track satisfies autoplay.
    requestPlay();
  });

  // Load waveform data for the current track.
  $effect(() => {
    const track = activeTrack;
    if (!track) {
      waveformData = null;
      return;
    }

    const waveformUrl =
      (track.waveformPath ? assetUrl(track.waveformPath) : null) ||
      assetUrl(`/assets/audio-tracks/${track.id}/waveform.json`);
    if (!waveformUrl) {
      waveformData = null;
      return;
    }

    let cancelled = false;
    fetch(waveformUrl, { cache: "no-store" })
      .then((response) => {
        if (!response.ok) throw new Error(`Waveform fetch failed: ${response.status}`);
        return response.json() as Promise<{ data?: number[] }>;
      })
      .then((payload) => {
        if (cancelled) return;
        waveformData = Array.isArray(payload.data) ? waveformForDisplay(payload.data) : null;
      })
      .catch(() => {
        if (!cancelled) waveformData = null;
      });

    return () => {
      cancelled = true;
    };
  });

  // Reserve layout space for the player bar so page content isn't hidden behind it.
  $effect(() => {
    const node = rootEl;
    if (!node) return;
    const update = () => chrome.setBottomDockInset("audio-player", node.getBoundingClientRect().height);
    update();
    const observer = new ResizeObserver(update);
    observer.observe(node);
    return () => {
      observer.disconnect();
      chrome.clearBottomDockInset("audio-player");
    };
  });

  onMount(() => {
    const audio = audioEl;
    if (!audio) return;

    const handleTimeUpdate = () => {
      if (!timelineDraggingRef) playback.currentTime = audio.currentTime;
    };
    const handleDurationChange = () => {
      if (Number.isFinite(audio.duration)) playback.duration = audio.duration;
    };
    const handlePlay = () => (playback.playing = true);
    const handlePause = () => (playback.playing = false);
    const handleEnded = () => {
      if (playback.currentTrack) recordTrackPlay(playback.currentTrack.id);
      handleTrackEnd();
    };
    const handleError = () => {
      playback.playing = false;
      console.error("Audio element error:", audio.error);
    };

    audio.addEventListener("timeupdate", handleTimeUpdate);
    audio.addEventListener("loadedmetadata", handleDurationChange);
    audio.addEventListener("durationchange", handleDurationChange);
    audio.addEventListener("play", handlePlay);
    audio.addEventListener("pause", handlePause);
    audio.addEventListener("ended", handleEnded);
    audio.addEventListener("error", handleError);
    const detachController = playback.attachController({ toggle: togglePlay, seek: handleSeek });

    return () => {
      audio.removeEventListener("timeupdate", handleTimeUpdate);
      audio.removeEventListener("loadedmetadata", handleDurationChange);
      audio.removeEventListener("durationchange", handleDurationChange);
      audio.removeEventListener("play", handlePlay);
      audio.removeEventListener("pause", handlePause);
      audio.removeEventListener("ended", handleEnded);
      audio.removeEventListener("error", handleError);
      detachController();
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

  const VolumeIcon = $derived(muted || volume === 0 ? VolumeX : volume < 0.5 ? Volume1 : Volume2);
</script>

<svelte:window onkeydown={handleKeydown} />

<!-- Hidden audio element -->
<audio bind:this={audioEl} preload="auto"></audio>

{#if collapsed}
  <!-- Collapsed: just the artwork with animated notes; tap to expand. -->
  <button
    bind:this={rootEl}
    type="button"
    onclick={() => (collapsed = false)}
    title="Expand player"
    aria-label="Expand audio player"
    class={cn(
      "audio-mini fixed bottom-[calc(3.65rem+max(1.25rem,env(safe-area-inset-bottom,0px))+0.5rem)] left-3 z-[55] h-14 w-14 overflow-visible rounded-xl border border-white/10 bg-surface-1/70 shadow-[0_14px_40px_rgba(0,0,0,0.55),inset_0_1px_0_rgba(255,255,255,0.07)] backdrop-blur-2xl transition-transform hover:scale-105 md:bottom-4 md:left-64",
    )}
  >
    {#if playing}
      <span class="audio-notes" aria-hidden="true">
        <Music2 class="audio-note audio-note-1 h-3 w-3" />
        <Music4 class="audio-note audio-note-2 h-3.5 w-3.5" />
        <Music class="audio-note audio-note-3 h-2.5 w-2.5" />
      </span>
    {/if}
    <span class="block h-full w-full overflow-hidden rounded-xl">
      {#if coverUrl}
        <img src={coverUrl} alt="" class="h-full w-full object-cover" decoding="async" />
      {:else}
        <span class="flex h-full w-full items-center justify-center bg-black/20 text-accent-500/80">
          <Music class="h-5 w-5" />
        </span>
      {/if}
    </span>
  </button>
{:else}
<div
  bind:this={rootEl}
  class={cn(
    "fixed bottom-[calc(3.65rem+max(1.25rem,env(safe-area-inset-bottom,0px))+0.5rem)] left-3 right-3 z-[55] mx-auto max-w-3xl rounded-xl border border-white/10 bg-surface-1/70 shadow-[0_18px_56px_rgba(0,0,0,0.6),inset_0_1px_0_rgba(255,255,255,0.07)] backdrop-blur-2xl md:bottom-4 md:left-64 md:right-4",
  )}
>
  <!-- Now-playing + progress -->
  <div class="flex items-center gap-2.5 px-3 pt-2.5 pb-1">
    <button
      type="button"
      onclick={collapse}
      title="Minimize player"
      aria-label="Minimize player"
      class="player-artwork relative h-9 w-9 shrink-0 overflow-hidden rounded-md transition-opacity hover:opacity-80"
    >
      {#if coverUrl}
        <img src={coverUrl} alt="" class="h-full w-full object-cover" decoding="async" />
      {:else}
        <div class="flex h-full w-full items-center justify-center bg-black/20 text-accent-500/80">
          <Music class="h-4 w-4" />
        </div>
      {/if}
    </button>

    <div class="min-w-0 flex-1">
      {#if activeTrack}
        <p class="truncate text-[0.8rem] font-medium leading-tight text-text-primary">{activeTrack.title}</p>
        <p class="truncate text-[0.68rem] leading-tight text-text-muted">
          {#if artistName && artistHref}
            <a href={artistHref} class="transition-colors hover:text-text-accent">{artistName}</a>
          {:else if artistName}
            {artistName}
          {:else}
            Unknown artist
          {/if}
          {#if albumLabel}
            <span class="text-text-disabled"> · {albumLabel}</span>
          {/if}
        </p>
      {:else}
        <p class="text-[0.8rem] text-text-muted">No track playing</p>
      {/if}
    </div>

    <span class="shrink-0 font-mono tabular-nums text-[0.65rem] text-text-disabled">
      {#if activeTrack}
        {formatDuration(currentTime) ?? "0:00"} / {formatDuration(duration) ?? "0:00"}
      {:else}
        --:--
      {/if}
    </span>

    <button
      type="button"
      onclick={dismiss}
      title="Close player"
      aria-label="Close player and clear queue"
      class="-mr-1 shrink-0 rounded-full p-1 text-text-disabled transition-colors hover:bg-white/5 hover:text-text-primary"
    >
      <X class="h-3.5 w-3.5" />
    </button>
  </div>

  <!-- Progress scrubber -->
  {#if activeTrack && duration > 0}
    <div class="mb-1 px-3">
      <!-- svelte-ignore a11y_no_static_element_interactions -->
      <div
        class="video-progress-track group/track overflow-hidden"
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
    </div>
  {/if}

  <!-- Waveform (only when data available) -->
  {#if activeTrack && waveformData && duration > 0}
    <div class="overflow-hidden border-t border-border-subtle/50 bg-black/30">
      <AudioWaveformFilmstrip peaks={waveformData} {duration} {audioEl} onSeek={handleSeek} />
    </div>
  {/if}

  <!-- Transport controls -->
  <div class="grid grid-cols-[1fr_auto_1fr] items-center gap-x-2 px-2 py-1.5">
    <div class="group/vol flex min-w-0 items-center gap-1">
      <button type="button" onclick={toggleMute} class="p-1 text-text-disabled transition-colors hover:text-text-muted">
        <VolumeIcon class="h-3 w-3" />
      </button>
      <div class="w-0 overflow-hidden transition-all duration-200 group-hover/vol:w-16">
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

    <div class="flex items-center gap-0.5">
      <button
        type="button"
        onclick={() => playback.toggleShuffle()}
        title={playback.shuffle ? "Shuffle: on" : "Shuffle: off"}
        class={cn("p-1.5 transition-colors", playback.shuffle ? "text-accent-500" : "text-text-disabled hover:text-text-muted")}
      >
        <Shuffle class="h-3 w-3" />
      </button>

      <button
        type="button"
        onclick={handlePrev}
        disabled={!activeTrack}
        class="p-1.5 text-text-muted transition-colors hover:text-text-primary disabled:text-text-disabled"
      >
        <SkipBack class="h-3.5 w-3.5" />
      </button>

      <button
        type="button"
        onclick={togglePlay}
        class={cn(
          "mx-0.5 rounded-full p-2 transition-all",
          playing
            ? "bg-accent-500 text-bg shadow-[0_0_10px_rgba(196,154,90,0.3)]"
            : "bg-accent-500/15 text-accent-300 ring-1 ring-accent-500/45 shadow-[0_0_12px_rgba(196,154,90,0.18)] hover:bg-accent-500 hover:text-bg",
        )}
      >
        {#if playing}
          <Pause class="h-4 w-4" />
        {:else}
          <Play class="ml-0.5 h-4 w-4" />
        {/if}
      </button>

      <button
        type="button"
        onclick={handleNext}
        disabled={!activeTrack || !playback.hasNext}
        class="p-1.5 text-text-muted transition-colors hover:text-text-primary disabled:text-text-disabled"
      >
        <SkipForward class="h-3.5 w-3.5" />
      </button>

      <button
        type="button"
        onclick={() => playback.cycleRepeat()}
        title={playback.repeat === "off" ? "Repeat: off" : playback.repeat === "all" ? "Repeat: all" : "Repeat: one"}
        class={cn("p-1.5 transition-colors", playback.repeat !== "off" ? "text-accent-500" : "text-text-disabled hover:text-text-muted")}
      >
        {#if playback.repeat === "one"}
          <Repeat1 class="h-3 w-3" />
        {:else}
          <Repeat class="h-3 w-3" />
        {/if}
      </button>
    </div>

    <div class="flex min-w-0 items-center justify-end gap-0.5">
      <button
        type="button"
        onclick={collapse}
        title="Minimize player"
        aria-label="Minimize player"
        class="p-1.5 text-text-disabled transition-colors hover:text-text-muted"
      >
        <Minimize2 class="h-3.5 w-3.5" />
      </button>
      <div class="relative">
        <button
          type="button"
          onclick={() => (queueOpen = !queueOpen)}
          title="Queue"
          class={cn("p-1.5 transition-colors", queueOpen ? "text-accent-500" : "text-text-disabled hover:text-text-muted")}
        >
          <ListMusic class="h-3.5 w-3.5" />
        </button>
        {#if queueOpen}
          <PlaybackQueueFlyout onClose={() => (queueOpen = false)} />
        {/if}
      </div>
    </div>
  </div>
</div>
{/if}

<style>
  /* Animated notes drifting out of the collapsed artwork while playing. */
  .audio-notes {
    position: absolute;
    left: 50%;
    top: 0;
    width: 0;
    height: 0;
    pointer-events: none;
  }
  .audio-notes :global(.audio-note) {
    position: absolute;
    color: #f2c26a;
    opacity: 0;
    filter: drop-shadow(0 0 4px rgba(242, 194, 106, 0.55));
    animation: audio-note-float 2.4s ease-out infinite;
  }
  .audio-notes :global(.audio-note-1) { left: -8px; animation-delay: 0s; }
  .audio-notes :global(.audio-note-2) { left: 0; animation-delay: 0.8s; }
  .audio-notes :global(.audio-note-3) { left: 8px; animation-delay: 1.6s; }

  @keyframes audio-note-float {
    0% { opacity: 0; transform: translate(0, 4px) scale(0.7) rotate(-8deg); }
    18% { opacity: 0.95; }
    100% { opacity: 0; transform: translate(9px, -28px) scale(1.05) rotate(10deg); }
  }

  @media (prefers-reduced-motion: reduce) {
    .audio-notes :global(.audio-note) { animation: none; opacity: 0; }
  }
</style>
