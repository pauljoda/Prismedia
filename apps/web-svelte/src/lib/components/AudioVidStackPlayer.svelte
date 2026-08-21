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
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { formatDuration } from "$lib/utils/format";
  import { recordEntityConsumptionEvent, updateEntityProgress } from "$lib/api/consumption";
  import { sendAudioPlaybackDiagnostic } from "$lib/api/audio-playback-diagnostics";
  import { apiAssetUrl, assetUrl } from "$lib/api/orval-fetch";
  import { paletteFromImage, type ArtworkPalette } from "$lib/entities/artwork-palette";
  import { resolveEntityHref } from "$lib/entities/entity-codes";
  import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
  import AudioWaveformFilmstrip from "./AudioWaveformFilmstrip.svelte";
  import PlaybackQueueFlyout from "./PlaybackQueueFlyout.svelte";
  import AudioTransportPreferenceControl from "./AudioTransportPreferenceControl.svelte";
  import { waveformForDisplay } from "./audio-waveform";
  import { ConsumptionActivityClock } from "$lib/entities/consumption-activity-clock";
  import { bookProgressUpdateForAudio } from "$lib/entities/book-combined-progress";
  import {
    AUDIO_PLAYBACK_SAVE_EVENT,
    resolveAudioArtwork,
    useAudioPlayback,
  } from "$lib/stores/audio-playback.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import {
    AUDIO_PLAYBACK_DIAGNOSTIC_EVENT,
    AUDIO_PLAYBACK_PAUSE_SOURCE,
    CONSUMPTION_EVENT_KIND,
    ENTITY_KIND,
    MUSIC_PLAYER_MINI_SIDE,
    MUSIC_PLAYER_REPEAT_MODE,
    type AudioPlaybackDiagnosticEventCode,
    type AudioPlaybackPauseSourceCode,
  } from "$lib/api/generated/codes";
  import { createAudioTabCoordinator, type AudioTabCoordinator } from "$lib/player/audio-tab-coordinator";
  import { AudioStreamRecoveryController } from "$lib/player/audio-stream-recovery";
  import { AudioPlaybackDiagnosticReporter } from "$lib/player/audio-playback-diagnostics";
  import {
    MusicConsumptionReporter,
    recordAudioConsumptionAccess,
  } from "$lib/player/music-consumption-reporter";
  import {
    setMediaSessionHandlers,
    setMediaSessionMetadata,
    setMediaSessionPlaybackState,
    setMediaSessionPosition,
  } from "$lib/media-session";

  const playback = useAudioPlayback()!;
  const chrome = useAppChrome();
  const QUICK_SKIP_THRESHOLD_SECONDS = 10;
  const AUDIOBOOK_PROGRESS_SAVE_INTERVAL_SECONDS = 5;
  const FALLBACK_PLAYER_PALETTE: ArtworkPalette = {
    primary: "#c7c9cc",
    secondary: "#8b8f96",
    background: "#090a0c",
  };

  let audioEl: HTMLAudioElement | null = $state(null);
  let rootEl: HTMLElement | null = $state(null);
  let waveformData = $state<number[] | null>(null);
  let timelineDragging = $state(false);
  let queueOpen = $state(false);
  let playbackRate = $state(1);
  let artworkPaletteState = $state<{ coverUrl: string; palette: ArtworkPalette } | null>(null);
  let timelineDraggingRef = false;
  let currentSrcTrackId: string | null = null;
  let audioStartedInThisSession = false;
  let currentTrackHasPlayed = false;
  let pendingInitialSeekSeconds: number | null = null;
  let pendingAutoplay: { trackId: string; deferWhenHidden: boolean } | null = null;
  let streamRecoverySequence = 0;
  let tabCoordinator: AudioTabCoordinator | null = null;
  let currentTrackRequestedAtMs: number | null = null;
  let lastAudiobookProgressSeconds: number | null = null;
  let lastAudiobookTrackId: string | null = null;
  let audiobookProgressSave = Promise.resolve();
  const audiobookActivityClock = new ConsumptionActivityClock();
  let audiobookAccessOwnerId: string | null = null;
  const musicConsumption = new MusicConsumptionReporter(() => ({
    positionSeconds: playback.currentTime,
    durationSeconds: playback.duration || activeTrack?.duration || null,
  }));
  const playbackDiagnostics = new AudioPlaybackDiagnosticReporter((diagnostic) => {
    void sendAudioPlaybackDiagnostic(diagnostic).catch(() => {});
  });
  const streamRecovery = new AudioStreamRecoveryController(({ trackId, positionSeconds }) => {
    recoverInterruptedStream(trackId, positionSeconds);
  });

  const activeTrack = $derived(playback.currentTrack);
  const ctx = $derived(playback.context);
  const currentTime = $derived(playback.currentTime);
  const duration = $derived(playback.duration);
  const playing = $derived(playback.playing);
  const volume = $derived(playback.volume);
  const muted = $derived(playback.muted);
  const collapsed = $derived(playback.collapsed);
  const preservesQueueOrder = $derived(ctx?.preservesQueueOrder === true);
  const supportsPlaybackRate = $derived(ctx?.supportsPlaybackRate === true);
  const hasBookProgress = $derived(
    Boolean(ctx?.playbackOwnerEntityId && ctx?.bookProgressMappings?.length),
  );
  const playbackOwnerHref = $derived(
    ctx?.playbackOwnerEntityId && ctx.playbackOwnerEntityKind
      ? resolveEntityHref(ctx.playbackOwnerEntityKind, ctx.playbackOwnerEntityId)
      : undefined,
  );
  const progress = $derived(
    duration > 0 ? Math.max(0, Math.min(100, (currentTime / duration) * 100)) : 0,
  );
  const artistName = $derived(
    ctx?.artistName ?? activeTrack?.embeddedArtist ?? activeTrack?.performers?.[0]?.name ?? null,
  );
  const artistHref = $derived(ctx?.artistId ? resolveEntityHref(ENTITY_KIND.musicArtist, ctx.artistId) : undefined);
  const coverUrl = $derived(resolveAudioArtwork(activeTrack, ctx));
  const playerPalette = $derived(
    artworkPaletteState?.coverUrl === coverUrl
      ? artworkPaletteState.palette
      : FALLBACK_PLAYER_PALETTE,
  );
  // Album label: a single-album context wins; otherwise fall back to the track's own album
  // so mixed-album queues (e.g. an artist Play All) still show the right album per track.
  const displayTitle = $derived(ctx?.playbackOwnerTitle ?? activeTrack?.title ?? null);
  const albumLabel = $derived(
    ctx?.supportsPlaybackRate === true
      ? activeTrack?.title ?? null
      : ctx?.albumTitle ?? activeTrack?.embeddedAlbum ?? null,
  );

  // Publish now-playing metadata to the OS media controls (lock screen, media keys, Bluetooth).
  $effect(() => {
    const track = activeTrack;
    if (!track) {
      setMediaSessionMetadata(null);
      return;
    }
    setMediaSessionMetadata({
      title: displayTitle ?? track.title,
      artist: artistName,
      album: albumLabel,
      artwork: coverUrl,
    });
  });

  // Keep the OS play/pause indicator in sync with the actual playback state.
  $effect(() => {
    setMediaSessionPlaybackState(activeTrack ? (playing ? "playing" : "paused") : "none");
  });

  function collapse() {
    playback.collapsed = true;
    queueOpen = false;
  }

  function handleArtworkLoad(event: Event) {
    const image = event.currentTarget as HTMLImageElement;
    const loadedCoverUrl = image.dataset.artworkUrl;
    if (
      !loadedCoverUrl ||
      loadedCoverUrl !== coverUrl ||
      artworkPaletteState?.coverUrl === loadedCoverUrl
    ) return;
    const palette = paletteFromImage(image);
    if (palette) artworkPaletteState = { coverUrl: loadedCoverUrl, palette };
  }

  function dismiss() {
    saveAudiobookProgress({ completed: false });
    pauseAudio(AUDIO_PLAYBACK_PAUSE_SOURCE.dismiss);
    tabCoordinator?.releasePlayback();
    playback.clear();
    window.dispatchEvent(new Event(AUDIO_PLAYBACK_SAVE_EVENT));
  }

  // --- Collapsed mini-player drag (fling left/right, snap with momentum) --------
  const MINI_WIDTH = 56; // h-14 / w-14
  let dragX = $state<number | null>(null); // live translateX while dragging / null = at rest
  let dragging = $state(false);
  let maxTravel = $state(0);
  let snapDuration = $state(0.42);

  let dragPointer: number | null = null;
  let dragStartX = 0;
  let dragStartTranslate = 0;
  let dragMoved = false;
  let lastX = 0;
  let lastT = 0;
  let velocity = 0; // px/ms, signed
  let suppressBubbleClick = false;

  const restTranslate = $derived(playback.collapsedSide === MUSIC_PLAYER_MINI_SIDE.right ? maxTravel : 0);
  const appliedTranslate = $derived(dragX ?? restTranslate);

  function computeMaxTravel(): number {
    if (typeof window === "undefined") return 0;
    const desktop = window.innerWidth >= 768;
    const leftBase = desktop ? 256 : 12; // md:left-64 (16rem) vs left-3 (0.75rem)
    const rightMargin = desktop ? 16 : 12; // md:right-4 vs right-3
    return Math.max(0, window.innerWidth - rightMargin - MINI_WIDTH - leftBase);
  }

  function bubblePointerDown(event: PointerEvent) {
    if (event.button !== 0) return;
    maxTravel = computeMaxTravel();
    dragPointer = event.pointerId;
    dragStartX = event.clientX;
    dragStartTranslate = restTranslate;
    dragMoved = false;
    lastX = event.clientX;
    lastT = event.timeStamp;
    velocity = 0;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
  }

  function bubblePointerMove(event: PointerEvent) {
    if (event.pointerId !== dragPointer) return;
    const dx = event.clientX - dragStartX;
    if (!dragMoved && Math.abs(dx) < 4) return;
    dragMoved = true;
    dragging = true;
    const dt = event.timeStamp - lastT;
    if (dt > 0) velocity = (event.clientX - lastX) / dt;
    lastX = event.clientX;
    lastT = event.timeStamp;
    dragX = Math.max(0, Math.min(maxTravel, dragStartTranslate + dx));
  }

  function bubblePointerUp(event: PointerEvent) {
    if (dragPointer !== null && event.pointerId !== dragPointer) return;
    const wasDrag = dragMoved;
    dragPointer = null;
    if (wasDrag) {
      const current = dragX ?? restTranslate;
      // Project the throw forward a little so a flick keeps its momentum into a side.
      const projected = current + velocity * 140;
      const goRight = Math.abs(velocity) > 0.35 ? velocity > 0 : projected > maxTravel / 2;
      // Snappier when thrown hard, gentler when nudged — both ease into place.
      snapDuration = Math.min(0.5, Math.max(0.2, 0.46 - Math.abs(velocity) * 0.22));
      playback.collapsedSide = goRight ? MUSIC_PLAYER_MINI_SIDE.right : MUSIC_PLAYER_MINI_SIDE.left;
      suppressBubbleClick = true;
      setTimeout(() => (suppressBubbleClick = false), 360);
    }
    dragging = false;
    dragX = null;
  }

  function bubbleClick() {
    if (suppressBubbleClick) {
      suppressBubbleClick = false;
      return;
    }
    playback.collapsed = false;
  }

  // Keep the docked position correct across viewport changes.
  $effect(() => {
    const onResize = () => {
      if (!dragging) maxTravel = computeMaxTravel();
    };
    onResize();
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  });

  function isKeyboardShortcutSuppressed(target: EventTarget | null): boolean {
    if (document.querySelector("[data-reader-overlay]")) return true;
    if (!(target instanceof HTMLElement)) return false;
    if (target.isContentEditable) return true;
    return Boolean(target.closest("input, textarea, select"));
  }

  function loadTrackSource(track: AudioTrackListItemDto): boolean {
    if (!audioEl) return false;
    if (track.id === currentSrcTrackId) return false;

    const nextSrc = apiAssetUrl(`/audio-stream/${track.id}`);
    if (!nextSrc) return false;

    musicConsumption.close();
    if (!audioEl.paused) {
      playbackDiagnostics.markPauseSource(AUDIO_PLAYBACK_PAUSE_SOURCE.trackChange);
    }
    const resumeTime = Math.max(0, playback.currentTime);
    currentSrcTrackId = track.id;
    currentTrackHasPlayed = false;
    streamRecovery.reset();
    streamRecoverySequence = 0;
    currentTrackRequestedAtMs = Date.now();
    musicConsumption.open(track.id);
    audioEl.src = nextSrc;
    playback.duration = track.duration ?? 0;
    pendingInitialSeekSeconds = resumeTime > 0 ? resumeTime : null;
    audioEl.load();
    if (pendingInitialSeekSeconds !== null) {
      try {
        audioEl.currentTime = pendingInitialSeekSeconds;
      } catch {
        // Metadata may not be available yet; loadedmetadata applies the same seek.
      }
    }
    return true;
  }

  function recoverInterruptedStream(trackId: string, interruptedAtSeconds: number) {
    const audio = audioEl;
    const track = activeTrack;
    if (!audio || !track || track.id !== trackId || currentSrcTrackId !== trackId) return;
    if (!playback.playIntent || audio.ended) return;

    const progressedSinceSignal = audio.currentTime > interruptedAtSeconds + 0.5;
    if (!audio.error && progressedSinceSignal && audio.readyState >= HTMLMediaElement.HAVE_FUTURE_DATA) {
      return;
    }

    const resumeTime = Math.max(0, audio.currentTime, playback.currentTime);
    streamRecoverySequence += 1;
    const nextSrc = apiAssetUrl(
      `/audio-stream/${trackId}`,
      `stream-recovery-${streamRecoverySequence}`,
    );
    if (!nextSrc) return;

    pendingInitialSeekSeconds = resumeTime;
    pendingAutoplay = { trackId, deferWhenHidden: false };
    audio.src = nextSrc;
    audio.load();
    try {
      audio.currentTime = resumeTime;
    } catch {
      // loadedmetadata applies the same seek once the replacement request is ready.
    }
  }

  function scheduleStreamRecovery(audio: HTMLAudioElement, terminalError = false) {
    const trackId = currentSrcTrackId;
    if (!trackId || !playback.playIntent || audio.ended) return;
    if (!terminalError && !currentTrackHasPlayed) return;
    streamRecovery.interrupt(
      { trackId, positionSeconds: Math.max(audio.currentTime, playback.currentTime) },
      terminalError,
    );
  }

  function canAttemptPlayback(options?: { deferWhenHidden?: boolean }): boolean {
    return (
      typeof document === "undefined" ||
      document.visibilityState === "visible" ||
      !options?.deferWhenHidden ||
      audioStartedInThisSession
    );
  }

  function requestPlay(
    expectedTrackId = currentSrcTrackId,
    options?: { deferWhenHidden?: boolean; stealActiveTab?: boolean },
  ) {
    if (!audioEl || !currentSrcTrackId) return;
    playback.playIntent = true;
    if (!canAttemptPlayback(options)) return;
    if (!tabCoordinator?.claimPlayback({ steal: options?.stealActiveTab ?? true })) return;

    const playPromise = audioEl.play();
    if (playPromise && typeof playPromise.catch === "function") {
      void playPromise.catch((error: unknown) => {
        console.error("Audio play failed:", error);
        if (expectedTrackId === currentSrcTrackId && audioEl?.paused) {
          playback.playing = false;
          if (error instanceof DOMException && error.name === "NotAllowedError") {
            playback.playIntent = false;
            streamRecovery.reset();
          } else if (playback.playIntent) {
            scheduleStreamRecovery(audioEl, true);
          }
        }
      });
    }
  }

  function playTrackNow(track: AudioTrackListItemDto) {
    loadTrackSource(track);
    requestPlay(track.id, { stealActiveTab: true });
  }

  function resetPlaybackPosition(nextDuration = 0) {
    pendingInitialSeekSeconds = null;
    pendingAutoplay = null;
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
    if (!timelineDraggingRef) saveAudiobookProgress({ completed: false });
    window.dispatchEvent(new Event(AUDIO_PLAYBACK_SAVE_EVENT));
  }

  function toggleMute() {
    if (!audioEl) return;
    audioEl.muted = !audioEl.muted;
    playback.muted = audioEl.muted;
  }

  function togglePlay() {
    if (!audioEl || !activeTrack) return;
    if (audioEl.paused) requestPlay();
    else pauseAudio(AUDIO_PLAYBACK_PAUSE_SOURCE.userControl);
  }

  function pauseAudio(
    source: AudioPlaybackPauseSourceCode,
    options: { clearPlayIntent?: boolean } = {},
  ) {
    const audio = audioEl;
    if (options.clearPlayIntent ?? true) {
      playback.playIntent = false;
      streamRecovery.reset();
    }
    if (!audio || audio.paused) return;
    playbackDiagnostics.markPauseSource(source);
    audio.pause();
  }

  function recordCurrentTrackSkip(track: AudioTrackListItemDto | null = activeTrack) {
    if (preservesQueueOrder) return;
    if (!track || !isQuickSkipCandidate()) return;
    void recordEntityConsumptionEvent(track.id, {
      kind: CONSUMPTION_EVENT_KIND.skipped,
      positionSeconds: playback.currentTime,
      durationSeconds: duration || track.duration || null,
    }).catch(() => {});
  }

  function isQuickSkipCandidate(): boolean {
    const positionSeconds = Math.max(0, playback.currentTime);
    const elapsedSinceRequestSeconds =
      currentTrackRequestedAtMs === null ? 0 : (Date.now() - currentTrackRequestedAtMs) / 1000;
    return (
      positionSeconds <= QUICK_SKIP_THRESHOLD_SECONDS &&
      elapsedSinceRequestSeconds <= QUICK_SKIP_THRESHOLD_SECONDS
    );
  }

  function jumpToQueuedTrack(orderIndex: number) {
    const skippedTrack = activeTrack;
    if (orderIndex === playback.position) return;
    saveAudiobookProgress({ completed: false });
    playback.jumpTo(orderIndex);
    if (playback.position !== orderIndex) return;
    recordCurrentTrackSkip(skippedTrack);
    resetPlaybackPosition(playback.currentTrack?.duration ?? 0);
    window.dispatchEvent(new Event(AUDIO_PLAYBACK_SAVE_EVENT));
  }

  function handleNext() {
    // The Next button advances even in repeat-one; the play position effect loads the new track.
    const skippedTrack = activeTrack;
    saveAudiobookProgress({ completed: false });
    if (playback.next()) {
      recordCurrentTrackSkip(skippedTrack);
      resetPlaybackPosition(playback.currentTrack?.duration ?? 0);
      window.dispatchEvent(new Event(AUDIO_PLAYBACK_SAVE_EVENT));
    }
  }

  function handlePrev() {
    saveAudiobookProgress({ completed: false });
    if (audioEl && audioEl.currentTime > 3) {
      resetPlaybackPosition(duration);
      window.dispatchEvent(new Event(AUDIO_PLAYBACK_SAVE_EVENT));
      return;
    }
    if (playback.prev()) {
      resetPlaybackPosition(playback.currentTrack?.duration ?? 0);
      window.dispatchEvent(new Event(AUDIO_PLAYBACK_SAVE_EVENT));
    }
  }

  function handleTrackEnd() {
    if (playback.repeat === MUSIC_PLAYER_REPEAT_MODE.one) {
      resetPlaybackPosition(duration);
      requestPlay();
      return;
    }
    if (playback.next()) {
      resetPlaybackPosition(playback.currentTrack?.duration ?? 0);
      window.dispatchEvent(new Event(AUDIO_PLAYBACK_SAVE_EVENT));
      return;
    }
    playback.playIntent = false;
    playback.playing = false;
    streamRecovery.reset();
    tabCoordinator?.releasePlayback();
  }

  function handleVolumeChange(nextVolume: number) {
    if (!audioEl) return;
    audioEl.volume = nextVolume;
    playback.volume = nextVolume;
    if (nextVolume > 0 && audioEl.muted) {
      audioEl.muted = false;
      playback.muted = false;
    }
  }

  function recordTrackPlay(trackId: string) {
    void recordEntityConsumptionEvent(trackId, {
      kind: CONSUMPTION_EVENT_KIND.completed,
      positionSeconds: playback.currentTime,
      durationSeconds: playback.duration || activeTrack?.duration || null,
    }).catch(() => {});
  }

  function startAudiobookConsumption() {
    const ownerId = ctx?.playbackOwnerEntityId;
    if (!ownerId) return;
    audiobookActivityClock.start();
    if (audiobookAccessOwnerId === ownerId) return;
    audiobookAccessOwnerId = ownerId;
    recordAudioConsumptionAccess(ownerId, playback.currentTime);
  }

  function isFinalAudiobookPart(): boolean {
    return hasBookProgress && playback.position === playback.order.length - 1;
  }

  function saveAudiobookProgress(options: {
    completed: boolean;
    periodic?: boolean;
    stopActivity?: boolean;
  }) {
    const ownerId = ctx?.playbackOwnerEntityId;
    const track = activeTrack;
    if (!hasBookProgress || !ownerId || !track) return;
    const mapping = ctx?.bookProgressMappings?.find((candidate) => candidate.trackId === track.id);
    if (!mapping) return;
    if (track.id !== lastAudiobookTrackId) {
      lastAudiobookTrackId = track.id;
      lastAudiobookProgressSeconds = null;
    }

    const durationSeconds = Math.max(0, playback.duration || track.duration || 0);
    if (durationSeconds <= 0) return;
    const positionSeconds = options.completed ? durationSeconds : playback.currentTime;
    if (
      options.periodic &&
      lastAudiobookProgressSeconds !== null &&
      Math.abs(positionSeconds - lastAudiobookProgressSeconds) < AUDIOBOOK_PROGRESS_SAVE_INTERVAL_SECONDS
    ) {
      return;
    }

    lastAudiobookProgressSeconds = positionSeconds;
    const activitySeconds = options.stopActivity
      ? audiobookActivityClock.stop()
      : playing
        ? audiobookActivityClock.take()
        : null;
    const update = bookProgressUpdateForAudio(
      mapping,
      positionSeconds,
      durationSeconds,
      activitySeconds,
      options.completed,
    );
    // Preserve seek/pause/part-transition ordering. Parallel writes can resolve backwards and move
    // the Book cursor to an older position even though the browser emitted events in the right order.
    audiobookProgressSave = audiobookProgressSave
      .catch(() => undefined)
      .then(() => updateEntityProgress(ownerId, update))
      .then(() => undefined)
      .catch(() => undefined);
  }

  // Switch audio source when the current track changes.
  $effect(() => {
    if (!audioEl) return;
    const track = activeTrack;
    if (!track) {
      currentSrcTrackId = null;
      currentTrackHasPlayed = false;
      streamRecovery.reset();
      currentTrackRequestedAtMs = null;
      audioEl.removeAttribute("src");
      audioEl.load();
      playback.playing = false;
      playback.currentTime = 0;
      playback.duration = 0;
      return;
    }

    const sourceChanged = loadTrackSource(track);
    if (playback.playIntent) {
      // Restored sessions may be blocked by browser autoplay policy; defer hidden-tab
      // restore attempts until the page is visible. Once audio has actually played in
      // this tab, continuing the queue while hidden should still try to start the
      // next track so background playback does not stall between songs.
      const deferWhenHidden = !audioStartedInThisSession;
      if (sourceChanged && pendingInitialSeekSeconds !== null) {
        pendingAutoplay = { trackId: track.id, deferWhenHidden };
      } else {
        requestPlay(track.id, { deferWhenHidden, stealActiveTab: false });
      }
    }
  });

  // Keep the element's audio settings in sync with persisted player preferences.
  $effect(() => {
    if (!audioEl) return;
    audioEl.volume = volume;
    audioEl.muted = muted;
  });

  $effect(() => {
    if (!audioEl) return;
    audioEl.playbackRate = supportsPlaybackRate ? playbackRate : 1;
  });

  // Load waveform data for the current track.
  $effect(() => {
    const track = activeTrack;
    if (!track || ctx?.supportsPlaybackRate === true) {
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

  // Reserve layout space for the full player bar so page content isn't hidden behind it.
  // The mini bubble floats in a corner and doesn't span the content, so it reserves nothing.
  $effect(() => {
    const node = rootEl;
    if (!node || collapsed) {
      chrome.clearBottomDockInset("audio-player");
      return;
    }
    const update = () => chrome.setBottomDockInset("audio-player", node.getBoundingClientRect().height);
    update();
    const observer = new ResizeObserver(update);
    observer.observe(node);
    return () => {
      observer.disconnect();
      chrome.clearBottomDockInset("audio-player");
    };
  });

  function applyPendingInitialSeek(audio: HTMLAudioElement) {
    if (pendingInitialSeekSeconds === null) return;
    const max =
      Number.isFinite(audio.duration) && audio.duration > 0
        ? audio.duration
        : duration > 0
          ? duration
          : Number.POSITIVE_INFINITY;
    const next = Math.max(0, Math.min(pendingInitialSeekSeconds, max));
    try {
      audio.currentTime = next;
    } catch (error) {
      console.warn("Failed to restore audio position:", error);
    }
    playback.currentTime = next;
    pendingInitialSeekSeconds = null;
  }

  function resumePendingAutoplay() {
    if (!pendingAutoplay || pendingInitialSeekSeconds !== null) return;
    const pending = pendingAutoplay;
    if (pending.trackId !== currentSrcTrackId) return;
    pendingAutoplay = null;
    requestPlay(pending.trackId, {
      deferWhenHidden: pending.deferWhenHidden,
      stealActiveTab: false,
    });
  }

  function bufferedAheadSeconds(audio: HTMLAudioElement): number {
    for (let index = 0; index < audio.buffered.length; index += 1) {
      const start = audio.buffered.start(index);
      const end = audio.buffered.end(index);
      if (audio.currentTime >= start && audio.currentTime <= end) {
        return Math.max(0, end - audio.currentTime);
      }
    }
    return 0;
  }

  function reportAudioDiagnostic(
    event: AudioPlaybackDiagnosticEventCode,
    audio: HTMLAudioElement,
  ) {
    const trackId = currentSrcTrackId;
    if (!trackId) return;
    playbackDiagnostics.report(event, {
      trackId,
      positionSeconds: audio.currentTime,
      durationSeconds: Number.isFinite(audio.duration) ? audio.duration : null,
      bufferedAheadSeconds: bufferedAheadSeconds(audio),
      readyState: audio.readyState,
      networkState: audio.networkState,
      paused: audio.paused,
      ended: audio.ended,
      playIntent: playback.playIntent,
      documentVisible: document.visibilityState === "visible",
      documentHasFocus: document.hasFocus(),
      mediaErrorCode: audio.error?.code ?? null,
    });
  }

  onMount(() => {
    const audio = audioEl;
    if (!audio) return;
    const coordinator = createAudioTabCoordinator();
    tabCoordinator = coordinator;

    const handleTimeUpdate = () => {
      if (!timelineDraggingRef) playback.currentTime = audio.currentTime;
      saveAudiobookProgress({ completed: false, periodic: true });
      setMediaSessionPosition(audio.duration, audio.currentTime, audio.playbackRate);
    };
    const handleDurationChange = () => {
      if (Number.isFinite(audio.duration)) {
        playback.duration = audio.duration;
        const track = activeTrack;
      }
      applyPendingInitialSeek(audio);
      setMediaSessionPosition(audio.duration, audio.currentTime, audio.playbackRate);
      resumePendingAutoplay();
    };
    const handlePlay = () => {
      audioStartedInThisSession = true;
      playback.playIntent = true;
      playback.playing = true;
      if (hasBookProgress) startAudiobookConsumption();
      else musicConsumption.start();
    };
    const handlePlaying = () => {
      currentTrackHasPlayed = true;
      streamRecovery.playing();
      reportAudioDiagnostic(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.playing, audio);
    };
    const handleWaiting = () => {
      reportAudioDiagnostic(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.waiting, audio);
      scheduleStreamRecovery(audio);
    };
    const handleStalled = () => {
      reportAudioDiagnostic(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.stalled, audio);
      scheduleStreamRecovery(audio);
    };
    const handlePause = () => {
      reportAudioDiagnostic(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.pause, audio);
      saveAudiobookProgress({
        completed: audio.ended && isFinalAudiobookPart(),
        stopActivity: true,
      });
      playback.playing = false;
      musicConsumption.pause();
      coordinator.releasePlayback();
      window.dispatchEvent(new Event(AUDIO_PLAYBACK_SAVE_EVENT));
    };
    const handleEnded = () => {
      musicConsumption.pause();
      if (hasBookProgress) {
        saveAudiobookProgress({ completed: isFinalAudiobookPart() });
      } else if (playback.currentTrack) {
        recordTrackPlay(playback.currentTrack.id);
      }
      handleTrackEnd();
      window.dispatchEvent(new Event(AUDIO_PLAYBACK_SAVE_EVENT));
    };
    const handleError = () => {
      reportAudioDiagnostic(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.error, audio);
      playback.playing = false;
      console.error("Audio element error:", audio.error);
      if (playback.playIntent) scheduleStreamRecovery(audio, true);
    };
    const handleVisibilityChange = () => {
      if (document.visibilityState !== "visible") return;
      if (!playback.playIntent || !playback.currentTrack || !audio.paused) return;
      requestPlay(currentSrcTrackId, { stealActiveTab: false });
    };
    const detachDisplaced = coordinator.onDisplaced(() => {
      pauseAudio(AUDIO_PLAYBACK_PAUSE_SOURCE.tabDisplaced);
      playback.playing = false;
    });

    audio.addEventListener("timeupdate", handleTimeUpdate);
    audio.addEventListener("loadedmetadata", handleDurationChange);
    audio.addEventListener("durationchange", handleDurationChange);
    audio.addEventListener("play", handlePlay);
    audio.addEventListener("playing", handlePlaying);
    audio.addEventListener("waiting", handleWaiting);
    audio.addEventListener("stalled", handleStalled);
    audio.addEventListener("pause", handlePause);
    audio.addEventListener("ended", handleEnded);
    audio.addEventListener("error", handleError);
    const activityTimer = window.setInterval(() => {
      if (!hasBookProgress && playback.playing) musicConsumption.heartbeat();
    }, 10_000);
    document.addEventListener("visibilitychange", handleVisibilityChange);
    audio.volume = playback.volume;
    audio.muted = playback.muted;
    const detachController = playback.attachController({
      toggle: togglePlay,
      seek: handleSeek,
      playTrack: playTrackNow,
    });
    // Wire OS media controls (lock screen, media keys, Bluetooth) to the play queue.
    // Deliberately omit seekbackward/seekforward: on iOS those skip buttons replace the
    // next/previous-track buttons, which a queue-based player needs. seekto still powers the
    // lock-screen scrubber without hiding next/previous.
    const detachMediaSession = setMediaSessionHandlers({
      play: requestPlay,
      pause: () => pauseAudio(AUDIO_PLAYBACK_PAUSE_SOURCE.mediaSession),
      previoustrack: handlePrev,
      nexttrack: handleNext,
      seekto: handleSeek,
      stop: dismiss,
    });

    return () => {
      audio.removeEventListener("timeupdate", handleTimeUpdate);
      audio.removeEventListener("loadedmetadata", handleDurationChange);
      audio.removeEventListener("durationchange", handleDurationChange);
      audio.removeEventListener("play", handlePlay);
      audio.removeEventListener("playing", handlePlaying);
      audio.removeEventListener("waiting", handleWaiting);
      audio.removeEventListener("stalled", handleStalled);
      audio.removeEventListener("pause", handlePause);
      audio.removeEventListener("ended", handleEnded);
      audio.removeEventListener("error", handleError);
      window.clearInterval(activityTimer);
      streamRecovery.reset();
      musicConsumption.close();
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      detachController();
      detachMediaSession();
      detachDisplaced();
      coordinator.destroy();
      if (tabCoordinator === coordinator) tabCoordinator = null;
      setMediaSessionMetadata(null);
    };
  });

  function handleKeydown(event: KeyboardEvent) {
    if (isKeyboardShortcutSuppressed(event.target)) return;
    if (!activeTrack) return;

    const seekBy = (delta: number) => {
      if (!audioEl) return;
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

</script>

<svelte:window onkeydown={handleKeydown} />

<!-- Hidden audio element -->
<audio bind:this={audioEl} preload="auto"></audio>

{#if activeTrack}
{#if collapsed}
  <!-- Collapsed: just the artwork with animated notes; tap to expand. -->
  <button
    bind:this={rootEl}
    type="button"
    onclick={bubbleClick}
    onpointerdown={bubblePointerDown}
    onpointermove={bubblePointerMove}
    onpointerup={bubblePointerUp}
    onpointercancel={bubblePointerUp}
    title="Expand player — drag to move"
    aria-label="Expand audio player"
    class={cn(
      "audio-mini fixed bottom-[calc(3.65rem+max(1.25rem,env(safe-area-inset-bottom,0px))+1.1rem)] left-3 z-[55] h-14 w-14 touch-none select-none overflow-visible rounded-xl border border-white/10 bg-surface-1/70 backdrop-blur-2xl md:bottom-4 md:left-64",
      dragging
        ? "shadow-[0_22px_60px_rgba(0,0,0,0.6),inset_0_1px_0_rgba(255,255,255,0.1)]"
        : "shadow-[0_14px_40px_rgba(0,0,0,0.55),inset_0_1px_0_rgba(255,255,255,0.07)]",
    )}
    style:transform={`translateX(${appliedTranslate}px) scale(${dragging ? 1.08 : 1})`}
    style:transition={dragging ? "none" : `transform ${snapDuration}s cubic-bezier(0.22, 1, 0.36, 1)`}
    style:cursor={dragging ? "grabbing" : "grab"}
    style:--player-accent={playerPalette.primary}
    style:--player-secondary={playerPalette.secondary}
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
        {#key coverUrl}
          <img
            src={coverUrl}
            data-artwork-url={coverUrl}
            alt=""
            class="h-full w-full object-cover"
            decoding="async"
            onload={handleArtworkLoad}
          />
        {/key}
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
    "audio-player fixed bottom-[calc(3.65rem+max(1.25rem,env(safe-area-inset-bottom,0px))+1.1rem)] left-3 right-3 z-[55] mx-auto max-w-3xl rounded-xl border shadow-[0_18px_56px_rgba(0,0,0,0.6),inset_0_1px_0_rgba(255,255,255,0.07)] md:bottom-4 md:left-64 md:right-4",
  )}
  style:--player-accent={playerPalette.primary}
  style:--player-secondary={playerPalette.secondary}
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
        {#key coverUrl}
          <img
            src={coverUrl}
            data-artwork-url={coverUrl}
            alt=""
            class="h-full w-full object-cover"
            decoding="async"
            onload={handleArtworkLoad}
          />
        {/key}
      {:else}
        <div class="flex h-full w-full items-center justify-center bg-black/20 text-accent-500/80">
          <Music class="h-4 w-4" />
        </div>
      {/if}
    </button>

    <div class="min-w-0 flex-1">
      {#if activeTrack}
        <p class="truncate text-[0.8rem] font-medium leading-tight text-text-primary">
          {#if playbackOwnerHref}
            <a href={playbackOwnerHref} class="player-link transition-colors">{displayTitle}</a>
          {:else}
            {displayTitle}
          {/if}
        </p>
        <p class="truncate text-[0.68rem] leading-tight text-text-muted">
          {#if artistName && artistHref}
            <a href={artistHref} class="player-link transition-colors">{artistName}</a>
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
      class="player-icon-control -mr-1 shrink-0 rounded-full p-1 transition-colors hover:bg-white/5"
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
          saveAudiobookProgress({ completed: false });
        }}
        onpointercancel={() => {
          timelineDraggingRef = false;
          timelineDragging = false;
        }}
      >
        <div class="video-progress-fill audio-progress-fill" style={`width: ${progress}%`}></div>
      </div>
    </div>
  {/if}

  <!-- Waveform (only when data available) -->
  {#if activeTrack && ctx?.supportsPlaybackRate !== true && waveformData && duration > 0}
    <div class="waveform-shell overflow-hidden border-t">
      <AudioWaveformFilmstrip
        peaks={waveformData}
        {duration}
        {audioEl}
        onSeek={handleSeek}
        accentPrimary={playerPalette.primary}
        accentSecondary={playerPalette.secondary}
      />
    </div>
  {/if}

  <!-- Transport controls -->
  <div class="grid grid-cols-[1fr_auto_1fr] items-center gap-x-2 px-2 py-1.5">
    <AudioTransportPreferenceControl
      accentColor={playerPalette.primary}
      {muted}
      {playbackRate}
      {supportsPlaybackRate}
      {volume}
      onMute={toggleMute}
      onPlaybackRate={(rate) => (playbackRate = rate)}
      onVolume={handleVolumeChange}
    />

    <div class="flex items-center gap-0.5">
      <button
        type="button"
        onclick={() => playback.toggleShuffle()}
        disabled={preservesQueueOrder}
        title={preservesQueueOrder ? "This queue plays in source order" : playback.shuffle ? "Shuffle: on" : "Shuffle: off"}
        class={cn("player-icon-control p-1.5 transition-colors", playback.shuffle && "player-icon-control--active")}
      >
        <Shuffle class="h-3 w-3" />
      </button>

      <button
        type="button"
        onclick={handlePrev}
        disabled={!activeTrack}
        class="player-icon-control p-1.5 transition-colors disabled:text-text-disabled"
      >
        <SkipBack class="h-3.5 w-3.5" />
      </button>

      <button
        type="button"
        onclick={togglePlay}
        class="player-play-button mx-0.5 rounded-full p-2 transition-all"
        data-playing={playing}
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
        class="player-icon-control p-1.5 transition-colors disabled:text-text-disabled"
      >
        <SkipForward class="h-3.5 w-3.5" />
      </button>

      <button
        type="button"
        onclick={() => playback.cycleRepeat()}
        title={playback.repeat === MUSIC_PLAYER_REPEAT_MODE.off ? "Repeat: off" : playback.repeat === MUSIC_PLAYER_REPEAT_MODE.all ? "Repeat: all" : "Repeat: one"}
        class={cn("player-icon-control p-1.5 transition-colors", playback.repeat !== MUSIC_PLAYER_REPEAT_MODE.off && "player-icon-control--active")}
      >
        {#if playback.repeat === MUSIC_PLAYER_REPEAT_MODE.one}
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
        class="player-icon-control p-1.5 transition-colors"
      >
        <Minimize2 class="h-3.5 w-3.5" />
      </button>
      <div class="relative">
        <button
          type="button"
          onclick={() => (queueOpen = !queueOpen)}
          title="Queue"
          class={cn("player-icon-control p-1.5 transition-colors", queueOpen && "player-icon-control--active")}
        >
          <ListMusic class="h-3.5 w-3.5" />
        </button>
        {#if queueOpen}
          <PlaybackQueueFlyout onClose={() => (queueOpen = false)} onJumpTo={jumpToQueuedTrack} />
        {/if}
      </div>
    </div>
  </div>
</div>
{/if}
{/if}

<style>
  .audio-mini {
    border-color: color-mix(in srgb, var(--player-accent) 34%, rgba(255, 255, 255, 0.08));
  }

  .audio-player {
    border-color: color-mix(in srgb, var(--player-accent) 26%, rgba(255, 255, 255, 0.08));
    background: var(--color-surface-1);
    transition: border-color 180ms var(--ease-default);
  }

  .player-link:hover,
  .player-icon-control.player-icon-control--active {
    color: color-mix(in srgb, var(--player-accent) 84%, white 12%);
  }

  .player-icon-control {
    color: var(--color-text-disabled);
  }

  .player-icon-control:hover:not(:disabled) {
    color: var(--color-text-muted);
  }

  .player-icon-control--active:hover:not(:disabled) {
    color: color-mix(in srgb, var(--player-accent) 92%, white 8%);
  }

  .player-play-button {
    color: color-mix(in srgb, var(--player-accent) 84%, white 12%);
    background: color-mix(in srgb, var(--player-accent) 16%, transparent);
    box-shadow:
      0 0 0 1px color-mix(in srgb, var(--player-accent) 46%, transparent),
      0 0 14px color-mix(in srgb, var(--player-accent) 22%, transparent);
  }

  .player-play-button:hover,
  .player-play-button[data-playing="true"] {
    color: var(--color-bg);
    background: color-mix(in srgb, var(--player-accent) 88%, white 8%);
    box-shadow: 0 0 16px color-mix(in srgb, var(--player-accent) 34%, transparent);
  }

  .audio-progress-fill {
    background: linear-gradient(
      90deg,
      color-mix(in srgb, var(--player-secondary) 72%, var(--player-accent)),
      var(--player-accent),
      color-mix(in srgb, var(--player-accent) 76%, white 20%)
    );
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.34),
      0 0 12px color-mix(in srgb, var(--player-accent) 50%, transparent);
  }

  .audio-player .video-progress-track {
    background: rgba(255, 255, 255, 0.15);
  }

  .audio-progress-fill::after {
    background: color-mix(in srgb, var(--player-accent) 78%, white 18%);
    box-shadow:
      0 0 10px color-mix(in srgb, var(--player-accent) 72%, transparent),
      0 2px 4px rgba(0, 0, 0, 0.5);
  }

  .waveform-shell {
    border-color: color-mix(in srgb, var(--player-accent) 16%, transparent);
    background: var(--color-bg);
  }

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
    left: 0;
    color: var(--player-accent, #c7c9cc);
    opacity: 0;
    filter: drop-shadow(0 0 4px color-mix(in srgb, var(--player-accent, #c7c9cc) 55%, transparent));
  }
  /* Three notes fan out evenly: one drifts left, one rises center, one drifts right. */
  .audio-notes :global(.audio-note-1) { animation: audio-note-left 2.4s ease-out infinite; animation-delay: 0s; }
  .audio-notes :global(.audio-note-2) { animation: audio-note-center 2.4s ease-out infinite; animation-delay: 0.8s; }
  .audio-notes :global(.audio-note-3) { animation: audio-note-right 2.4s ease-out infinite; animation-delay: 1.6s; }

  /* translateX(-50% …) self-centers each note on the spawn point regardless of its
     own width, so the cluster originates from the true upper-center of the artwork. */
  @keyframes audio-note-left {
    0% { opacity: 0; transform: translate(-50%, 4px) scale(0.7) rotate(6deg); }
    18% { opacity: 0.95; }
    100% { opacity: 0; transform: translate(calc(-50% - 16px), -26px) scale(1.05) rotate(-14deg); }
  }
  @keyframes audio-note-center {
    0% { opacity: 0; transform: translate(-50%, 4px) scale(0.7) rotate(-4deg); }
    18% { opacity: 0.95; }
    100% { opacity: 0; transform: translate(-50%, -32px) scale(1.1) rotate(6deg); }
  }
  @keyframes audio-note-right {
    0% { opacity: 0; transform: translate(-50%, 4px) scale(0.7) rotate(-6deg); }
    18% { opacity: 0.95; }
    100% { opacity: 0; transform: translate(calc(-50% + 16px), -26px) scale(1.05) rotate(14deg); }
  }

  @media (prefers-reduced-motion: reduce) {
    .audio-notes :global(.audio-note) { animation: none; opacity: 0; }
  }
</style>
