<script module lang="ts">
  export type {
    ActiveCue,
    VideoPlayerAudioTrack,
    VideoPlayerHandle,
    VideoPlayerMarker,
  } from "./video-player-types";
</script>

<script lang="ts">
  import "vidstack/player";
  import "vidstack/player/ui";

  import { onMount } from "svelte";
  import {
    Cast,
    Loader,
    Maximize,
    Play,
    PanelRightOpen,
    Settings2,
    Volume2,
    VolumeX,
  } from "@lucide/svelte";
  import {
    cn,
    findFrameAtTime,
    loadTrickplayFrames,
    type TrickplayFrame,
  } from "@prismedia/ui-svelte";
  import {
    isHLSProvider,
    type AudioTrack,
    type MediaCanPlayEvent,
    type MediaErrorEvent,
    type MediaProviderChangeEvent,
    type MediaTimeUpdateEvent,
    TextTrack,
    type VTTCueInit,
    type VideoQuality,
  } from "vidstack";
  import type { MediaPlayerElement } from "vidstack/elements";
  import {
    type SubtitleAppearance,
    type SubtitleCue,
    type VideoSubtitleTrack,
  } from "$lib/player/subtitle-types";
  import { fetchVideoSubtitleCues } from "$lib/player/video-subtitles";
  import {
    enterMediaFullscreen,
    exitDocumentFullscreen,
    isDocumentFullscreen,
  } from "$lib/fullscreen";
  import { buildMediaArtwork } from "$lib/media-session";
  import {
    adaptiveHlsBufferConfig,
    canUseDirectPlayback,
    fallbackPlaybackModeForError,
    hlsStatusUrlForSrc,
  } from "$lib/player/video-player-load";
  import { playbackMethodBadge, resolutionBadge, type StreamMethod } from "$lib/player/media-badges";
  import {
    readQualityPreference,
    writeQualityPreference,
  } from "$lib/player/quality-preference";
  import { resolveInitialVideoPlayerSourcePolicy } from "$lib/player/video-player-source-policy";
  import {
    pickPreferredSubtitleTrack,
    readLocalSubtitleAppearance,
    resolveSubtitleAppearance,
    writeLocalSubtitleAppearance,
  } from "$lib/player/subtitle-appearance";
  import {
    buildTimelineChapterCues,
    findTimelineChapterTitle,
  } from "$lib/player/timeline-chapters";
  import {
    formatTime,
    formatBandwidth,
    formatDimensions,
    languageLabel,
  } from "./video-player-format";
  import {
    GOOGLE_CAST_SENDER_URL,
    HLS_RETRY_AFTER_SECONDS,
    MARKER_CHAPTERS_TRACK_ID,
    type ActiveCue,
    type AudioTrackOption,
    type CastWindow,
    type HlsStatus,
    type PlaybackMode,
    type PlayerQualityRung,
    type QualityMode,
    type QualityOption,
    type SettingsView,
    type VideoPlayerAudioTrack,
    type VideoPlayerHandle,
    type VideoPlayerMarker,
  } from "./video-player-types";
  import FilmStrip from "./FilmStrip.svelte";
  import VideoSettingsMenu from "./VideoSettingsMenu.svelte";
  import VideoStatusBar from "./VideoStatusBar.svelte";
  import VideoSubtitleOverlay from "./VideoSubtitleOverlay.svelte";
  import VideoTimeline from "./VideoTimeline.svelte";
  import VideoTransportControls from "./VideoTransportControls.svelte";

  interface Props {
    src?: string;
    directSrc?: string;
    codec?: string | null;
    sourceWidth?: number | null;
    sourceHeight?: number | null;
    colorPipelineLabel?: string | null;
    /** Marketing resolution tier of the source ("4K", "1080p", …) for the status badge. */
    resolutionLabel?: string | null;
    /** Friendly HDR format of the source ("Dolby Vision", "HDR10", …), or null for SDR. */
    dynamicRangeLabel?: string | null;
    /** Source video codec as viewers know it ("HEVC", "H.264", …). */
    videoCodecLabel?: string | null;
    /** Default audio track's format descriptor ("Dolby Atmos 7.1", …) for the status badge. */
    audioFormatLabel?: string | null;
    /** The server's negotiated delivery method, before any client-side fallback. */
    streamMethod?: StreamMethod;
    /** Manual quality tiers the viewer can pin (Jellyfin-style), each a ready-to-load variant URL. */
    qualityRungs?: PlayerQualityRung[];
    poster?: string;
    /** Title of the playing media, published to the OS media controls via the Media Session API. */
    mediaTitle?: string;
    /** Series, show, or studio shown as the "artist" on the OS media controls. */
    mediaArtist?: string;
    markers?: VideoPlayerMarker[];
    duration?: number;
    onMarkerClick?: (marker: VideoPlayerMarker) => void;
    onCanPlay?: () => void;
    onPlayStarted?: () => void;
    onTimeUpdate?: (time: number) => void;
    trickplayPlaylist?: string;
    subtitleTracks?: VideoSubtitleTrack[];
    audioTrackOptions?: VideoPlayerAudioTrack[];
    onAudioTrackChange?: (streamIndex: number) => void | Promise<void>;
    activeSubtitleTrackId?: string | null;
    onActiveSubtitleTrackIdChange?: (id: string | null) => void;
    onActiveCueChange?: (cue: ActiveCue | null) => void;
    subtitleChoiceLocked?: boolean;
    subtitleDefaults?: {
      autoEnable: boolean;
      preferredLanguages: string;
      appearance: SubtitleAppearance;
    };
    isTranscriptSidecarOpen?: boolean;
    onTranscriptSidecarToggle?: () => void;
    defaultPlaybackMode?: "direct" | "hls";
    /**
     * Last-resort recovery for a fatal decode/MSE error. Invoked after direct↔HLS
     * fallback is exhausted (e.g. the server remuxed a stream the browser cannot
     * actually decode). Should re-negotiate PlaybackInfo with direct play and
     * stream-copy disabled and return a guaranteed-playable transcode URL, or null
     * if no compatible stream is available. Mirrors Jellyfin's re-request-with-
     * DirectPlay-off recovery so anything that fails to decode escalates to H.264.
     */
    onForceTranscode?: (atSeconds: number) => Promise<string | null>;
    showCastControls?: boolean;
    chrome?: "full" | "minimal";
    enableKeyboardShortcuts?: boolean;
    initialMuted?: boolean;
    onEnded?: () => void;
    autoPlay?: boolean;
    autoRepeat?: boolean;
    handle?: VideoPlayerHandle;
  }

  let {
    src,
    directSrc,
    codec,
    sourceWidth = null,
    sourceHeight = null,
    colorPipelineLabel = null,
    resolutionLabel = null,
    dynamicRangeLabel = null,
    videoCodecLabel = null,
    audioFormatLabel = null,
    streamMethod = "transcode",
    qualityRungs = [],
    poster,
    mediaTitle,
    mediaArtist,
    markers = [],
    duration: propDuration,
    onMarkerClick,
    onCanPlay,
    onPlayStarted,
    onTimeUpdate,
    trickplayPlaylist,
    subtitleTracks = [],
    audioTrackOptions = [],
    onAudioTrackChange,
    activeSubtitleTrackId: controlledSubtitleId,
    onActiveSubtitleTrackIdChange,
    onActiveCueChange,
    subtitleChoiceLocked = false,
    subtitleDefaults,
    isTranscriptSidecarOpen = false,
    onTranscriptSidecarToggle,
    defaultPlaybackMode,
    onForceTranscode,
    showCastControls = true,
    chrome = "full",
    enableKeyboardShortcuts = true,
    initialMuted = false,
    onEnded,
    autoPlay = false,
    autoRepeat = false,
    handle = $bindable(),
  }: Props = $props();

  let containerEl: HTMLDivElement | undefined = $state();
  let player: MediaPlayerElement | undefined = $state();
  let videoEl: HTMLVideoElement | null = $state(null);

  // Feed VidStack an absolute artwork URL for the OS Media Session. VidStack otherwise falls back to
  // the (root-relative) poster, which the OS Now-Playing handler cannot resolve, so the thumbnail
  // never appears on the lock screen / media controls.
  $effect(() => {
    if (!player) return;
    player.artwork = buildMediaArtwork(poster);
  });
  let directCapabilityProbe: HTMLVideoElement | null = $state(null);
  let mediaMounted = $state(false);
  let controlsTimeout: number | null = null;
  let initialMutedSyncTimer: number | null = null;
  let playTracked = false;
  let endedTracked = false;
  let lastSourceKey = "";
  let muteTouched = false;
  let lastNonZeroVolume = 1;
  let pendingSeekTime: number | null = null;
  let pendingAutoPlay = $state(false);
  let markerChaptersTrack: TextTrack | null = null;
  let hlsReadySrc: string | undefined = $state();
  let failedDirectSrc = $state<string | null>(null);
  // Last-resort transcode URL obtained via onForceTranscode after direct↔HLS fallback fails.
  // When set it overrides the HLS source; reset whenever a genuinely new source loads.
  let forcedTranscodeSrc = $state<string | null>(null);
  let forceTranscodeRequested = false;
  // A single play() retry guard. A play() rejection on first attempt is usually transient (the
  // source is still spinning up, or the request was interrupted by a load); we retry once rather
  // than treating it as a fatal error. Reset on each new source.
  let playRetried = false;

  let playbackMode = $state<PlaybackMode>("hls");
  let qualityMode = $state<QualityMode>("auto");
  // The name of the manually-pinned quality tier, or null for server-chosen ("Auto").
  let selectedRungName = $state<string | null>(null);
  let currentTime = $state(0);
  let duration = $state(0);
  let playing = $state(false);
  let buffering = $state(false);
  // svelte-ignore state_referenced_locally
  let muted = $state(initialMuted);
  let volume = $state(1);
  let playbackRate = $state(1);
  let showControls = $state(true);
  let bufferAhead = $state(0);
  let activeQualityLabel = $state<string | null>(null);
  let activeQualityDimensionsLabel = $state<string | null>(null);
  let activeQualityResolutionLabel = $state<string | null>(null);
  let audioTracks = $state<AudioTrackOption[]>([]);
  let selectedAudioTrackLabel = $state<string | null>(null);
  let playerNotice = $state<string | null>(null);
  let timelineHover = $state<{
    chapterTitle: string | null;
    markerTitles: string[];
    percent: number;
    time: number;
  } | null>(null);
  let timelineTrickplayFrames = $state<TrickplayFrame[] | null>(null);
  let timelineTrickplayError = $state(false);

  let settingsMenuRendered = $state(false);
  let settingsMenuClosing = $state(false);
  let settingsView = $state<SettingsView>("root");
  let settingsCloseTimer: number | null = null;

  let internalSubtitleId = $state<string | null>(null);
  let activeTrackCues = $state<SubtitleCue[]>([]);
  let activeCueText = $state<string | null>(null);
  let localAppearance = $state<Partial<SubtitleAppearance> | null>(null);
  let autoSelected = false;
  let autoSelectionKey = "";
  let googleCastFrameworkPromise: Promise<boolean> | null = null;
  let googleCastFrameworkAvailable = false;

  const directPlayable = $derived.by(() => {
    const video = (videoEl ?? directCapabilityProbe) as HTMLVideoElement | null;
    return canUseDirectPlayback({
      directSrc,
      codec,
      canPlayType: video ? video.canPlayType.bind(video) : undefined,
    });
  });
  const requiresServerAudioSelection = $derived(audioTrackOptions.length > 1);
  const directAvailable = $derived(Boolean(
    directSrc && directPlayable && failedDirectSrc !== directSrc && !requiresServerAudioSelection,
  ));
  const effectiveMode = $derived<PlaybackMode>(
    playbackMode === "direct" && directAvailable ? "direct" : "hls",
  );
  // The active HLS URL: a forced-transcode URL (recovery) takes precedence over the negotiated src.
  // A manually-pinned quality tier streams its own variant playlist; otherwise the server-chosen src.
  const selectedRungUrl = $derived(
    qualityRungs.find((rung) => rung.name === selectedRungName)?.url ?? null,
  );
  const effectiveHlsSrc = $derived(forcedTranscodeSrc ?? selectedRungUrl ?? src);
  const requestedPlayerSrc = $derived(effectiveMode === "direct" ? directSrc : effectiveHlsSrc);
  const playerSrc = $derived(requestedPlayerSrc === hlsReadySrc ? requestedPlayerSrc : undefined);
  const hasFilmStrip = $derived(Boolean(trickplayPlaylist && duration > 0));
  const markerChapterCues = $derived(buildTimelineChapterCues(markers, duration));
  const timelinePreviewFrame = $derived.by(() => {
    if (
      timelineTrickplayError ||
      !timelineHover ||
      !timelineTrickplayFrames ||
      timelineTrickplayFrames.length === 0
    ) {
      return null;
    }
    return timelineTrickplayFrames[findFrameAtTime(timelineTrickplayFrames, timelineHover.time)] ?? null;
  });
  const timelinePreviewSpriteDims = $derived.by(() => {
    if (!timelineTrickplayFrames) return { width: 0, height: 0 };
    return {
      width: timelineTrickplayFrames.reduce((max, frame) => Math.max(max, frame.x + frame.width), 0),
      height: timelineTrickplayFrames.reduce((max, frame) => Math.max(max, frame.y + frame.height), 0),
    };
  });
  const activeSubtitleId = $derived(
    controlledSubtitleId !== undefined ? controlledSubtitleId : internalSubtitleId,
  );
  const appearance = $derived(
    resolveSubtitleAppearance(subtitleDefaults?.appearance ?? null, localAppearance),
  );
  // Quality menu: Direct (when the original plays as-is) + Auto + each manual tier, highest first.
  const qualityOptions = $derived<QualityOption[]>([
    ...(directAvailable ? [{ value: "direct" as const, label: "Direct" }] : []),
    { value: "auto" as const, label: "Auto" },
    ...qualityRungs.map((rung) => ({ value: rung.name, label: rung.label })),
  ]);
  const pinnedRungLabel = $derived(
    qualityRungs.find((rung) => rung.name === selectedRungName)?.label ?? null,
  );
  const selectedQualityLabel = $derived(
    effectiveMode === "direct"
      ? "Direct"
      : pinnedRungLabel ??
        `Auto${activeQualityResolutionLabel ? ` · ${activeQualityResolutionLabel}` : ""}`,
  );
  const sourceResolutionLabel = $derived(formatDimensions(sourceWidth, sourceHeight));
  // Prefer the negotiated tier, but stay self-sufficient by deriving it from the raw dimensions.
  const resolutionBadgeLabel = $derived(resolutionLabel ?? resolutionBadge(sourceWidth, sourceHeight));
  // What is actually happening to the stream right now: a forced recovery transcode wins, then the
  // player's own direct/HLS choice, otherwise the server's negotiated plan. A server "direct" plan
  // while the player is on HLS still means the picture is copied (stream-copy), i.e. "Direct Stream".
  const playbackMethod = $derived<StreamMethod>(
    forcedTranscodeSrc
      ? "transcode"
      : effectiveMode === "direct"
        ? "direct"
        : streamMethod === "direct"
          ? "remux"
          : streamMethod,
  );
  const playbackMethodLabel = $derived(playbackMethodBadge(playbackMethod).label);
  const playbackMethodHint = $derived(playbackMethodBadge(playbackMethod).hint);
  // When transcoding, show the output the viewer is getting (quality, and SDR when tone-mapped).
  const playbackMethodDetail = $derived.by(() => {
    if (playbackMethod !== "transcode") return null;
    // The server always transcodes to H.264, tone-mapping HDR down to SDR; show that real output.
    const parts: string[] = [];
    if (activeQualityResolutionLabel) parts.push(activeQualityResolutionLabel);
    parts.push("H.264");
    if (dynamicRangeLabel) parts.push("SDR");
    return parts.join(" · ");
  });
  // The audio format of the track the viewer is hearing, tracking selection when there are several.
  const activeAudioFormatLabel = $derived(
    audioTrackOptions.find((track) => track.selected)?.formatLabel ?? audioFormatLabel,
  );
  // Tooltip detail for the video badge: exact pixels and source codec for anyone who wants it.
  const videoBadgeDetail = $derived(
    [sourceResolutionLabel, videoCodecLabel].filter(Boolean).join(" · ") || null,
  );
  const fullChrome = $derived(chrome === "full");
  const playbackProgressPercent = $derived(
    duration > 0 ? Math.max(0, Math.min(100, (currentTime / duration) * 100)) : 0,
  );
  const bufferedProgressPercent = $derived(
    duration > 0
      ? Math.max(
          playbackProgressPercent,
          Math.max(0, Math.min(100, ((currentTime + bufferAhead) / duration) * 100)),
        )
      : 0,
  );
  const displayedAudioTracks = $derived<AudioTrackOption[]>(
    audioTrackOptions.length > 1
      ? audioTrackOptions.map((track) => ({
          id: track.id,
          index: -1,
          streamIndex: track.streamIndex,
          label: track.label,
          selected: track.selected,
          source: "external" as const,
        }))
      : audioTracks.length > 0
        ? audioTracks
        : [
            {
              id: "default-audio",
              index: -1,
              streamIndex: null,
              label: "Default audio",
              selected: true,
              source: "external",
            },
          ],
  );
  const displayedAudioTrackLabel = $derived(
    selectedAudioTrackLabel ?? displayedAudioTracks.find((track) => track.selected)?.label ?? "Audio",
  );
  const activeSubtitleLabel = $derived.by(() => {
    if (!activeSubtitleId) return "Off";
    const track = subtitleTracks.find((candidate) => candidate.id === activeSubtitleId);
    if (!track) return "On";
    const lang = languageLabel(track.language);
    return track.label ? `${lang} - ${track.label}` : lang;
  });

  const assTrackForRender = $derived.by(() => {
    if (!activeSubtitleId) return null;
    const track = subtitleTracks.find((t) => t.id === activeSubtitleId);
    if (!track) return null;
    if (track.sourceFormat !== "ass" && track.sourceFormat !== "ssa") return null;
    if (!track.sourceUrl) return null;
    return track;
  });
  const showTextSubtitleCue = $derived(
    Boolean(activeCueText && !isAssTrackActive(activeSubtitleId, subtitleTracks)),
  );

  function isAssTrackActive(
    id: string | null | undefined,
    tracks: readonly VideoSubtitleTrack[],
  ): boolean {
    if (!id) return false;
    const track = tracks.find((x) => x.id === id);
    if (!track) return false;
    return (track.sourceFormat === "ass" || track.sourceFormat === "ssa") && !!track.sourceUrl;
  }

  function mediaElement() {
    const video = videoEl ?? player?.querySelector("video") ?? containerEl?.querySelector("video") ?? null;
    if (video && video !== videoEl) videoEl = video;
    return video;
  }

  function syncVideoElement() {
    const video = player?.querySelector("video") ?? containerEl?.querySelector("video") ?? null;
    if (video !== videoEl) videoEl = video;
    return video;
  }

  function closeMenus() {
    closeSettings();
  }

  function clearControlsTimer() {
    if (controlsTimeout) {
      window.clearTimeout(controlsTimeout);
      controlsTimeout = null;
    }
  }

  function clearSettingsCloseTimer() {
    if (settingsCloseTimer) {
      window.clearTimeout(settingsCloseTimer);
      settingsCloseTimer = null;
    }
  }

  function scheduleControlsHide() {
    clearControlsTimer();
    if (!playing) return;
    controlsTimeout = window.setTimeout(() => {
      showControls = false;
      closeMenus();
    }, 2400);
  }

  function surfaceControls() {
    showControls = true;
    scheduleControlsHide();
  }

  function updateBuffered() {
    const video = mediaElement();
    if (!video || duration <= 0) {
      bufferAhead = 0;
      return;
    }
    let bufferedEnd = 0;
    for (let i = 0; i < video.buffered.length; i += 1) {
      const start = video.buffered.start(i);
      const end = video.buffered.end(i);
      if (video.currentTime >= start && video.currentTime <= end) {
        bufferedEnd = end;
        break;
      }
      bufferedEnd = Math.max(bufferedEnd, end);
    }
    bufferAhead = Math.max(0, bufferedEnd - video.currentTime);
  }

  function notifyPlaybackEnded() {
    if (endedTracked) return;
    endedTracked = true;
    onEnded?.();
  }

  function qualityLabel(quality: VideoQuality, index: number) {
    if (quality.bitrate) return formatBandwidth(quality.bitrate);
    return `Level ${index + 1}`;
  }

  function qualityDimensionsLabel(quality: VideoQuality) {
    return formatDimensions(quality.width, quality.height);
  }

  // Tracks the actually-playing rung's resolution (for the "Transcoding → 1080p" detail and the Auto
  // label). The selectable tiers themselves are derived from qualityRungs, not from hls.js levels.
  function refreshQualities() {
    if (!player || effectiveMode === "direct") {
      activeQualityLabel = null;
      activeQualityDimensionsLabel = null;
      activeQualityResolutionLabel = null;
      return;
    }
    const qualities = player.qualities?.toArray?.() ?? [];
    const selected = qualities.find((quality) => quality.selected);
    if (!selected) return;
    activeQualityLabel = qualityLabel(selected, qualities.indexOf(selected));
    activeQualityDimensionsLabel = qualityDimensionsLabel(selected);
    activeQualityResolutionLabel = resolutionBadge(selected.width, selected.height);
  }

  function audioTrackLabel(track: AudioTrack, index: number): string {
    const label = track.label || track.language || track.kind || `Track ${index + 1}`;
    return track.language && !label.toLowerCase().includes(track.language.toLowerCase())
      ? `${label} · ${track.language}`
      : label;
  }

  function refreshAudioTracks() {
    const tracks = player?.audioTracks?.toArray?.() ?? [];
    audioTracks = tracks.map((track, index) => ({
      id: track.id,
      index,
      streamIndex: null,
      label: audioTrackLabel(track, index),
      selected: track.selected,
      source: "native",
    }));
    selectedAudioTrackLabel =
      audioTracks.find((track) => track.selected)?.label ?? audioTracks[0]?.label ?? null;
  }

  function selectAudioTrack(trackOption: AudioTrackOption) {
    if (trackOption.source === "external") {
      if (trackOption.streamIndex !== null) {
        pendingSeekTime = currentTime > 0.25 ? currentTime : null;
        pendingAutoPlay = playing;
        selectedAudioTrackLabel = trackOption.label;
        void onAudioTrackChange?.(trackOption.streamIndex);
      }
      closeMenus();
      return;
    }
    if (trackOption.index < 0) {
      selectedAudioTrackLabel = displayedAudioTrackLabel;
      closeMenus();
      return;
    }
    const track = player?.audioTracks?.toArray?.()[trackOption.index];
    if (!track) return;
    track.selected = true;
    selectedAudioTrackLabel = audioTrackLabel(track, trackOption.index);
    refreshAudioTracks();
    closeMenus();
  }

  // Quality selection swaps the stream source (Direct, server-chosen Auto, or a pinned tier) while
  // holding the playback position; handleCanPlay restores pendingSeekTime once the new stream loads.
  function requestPlaybackMode(nextQualityMode: QualityMode) {
    pendingSeekTime = currentTime > 0.25 ? currentTime : null;
    pendingAutoPlay = playing;
    closeMenus();
    if (nextQualityMode === "direct") {
      playbackMode = "direct";
      qualityMode = "direct";
      selectedRungName = null;
      writeQualityPreference("direct");
      return;
    }
    playbackMode = "hls";
    qualityMode = nextQualityMode;
    if (nextQualityMode === "auto") {
      selectedRungName = null;
      writeQualityPreference("auto");
      return;
    }
    // A specific tier was pinned: stream its variant and remember the cap for future videos.
    selectedRungName = nextQualityMode;
    const rung = qualityRungs.find((candidate) => candidate.name === nextQualityMode);
    writeQualityPreference(rung ? rung.bitrate : "auto");
  }

  function selectSubtitle(id: string | null) {
    if (controlledSubtitleId === undefined) internalSubtitleId = id;
    onActiveSubtitleTrackIdChange?.(id);
    closeMenus();
  }


  function handleAppearanceChange(next: SubtitleAppearance) {
    localAppearance = next;
    writeLocalSubtitleAppearance(next);
  }

  function handleAppearanceReset() {
    localAppearance = null;
    writeLocalSubtitleAppearance(null);
  }

  function togglePlay() {
    if (!player) return;
    const video = mediaElement();
    if (player.paused && video?.paused !== false) void playWithFallback();
    else {
      // Clear any deferred play intent so pausing while the media is still loading actually stays
      // paused (a pending intent would otherwise resume it once 'canplay' fires).
      pendingAutoPlay = false;
      void player.pause();
      mediaElement()?.pause();
      playing = false;
      showControls = true;
      clearControlsTimer();
    }
  }

  function handleMinimalSurfaceClick(event: MouseEvent) {
    if (!fullChrome) {
      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();
      togglePlay();
    }
  }

  function clearInitialMutedSync() {
    if (!initialMutedSyncTimer) return;
    window.clearTimeout(initialMutedSyncTimer);
    initialMutedSyncTimer = null;
  }

  function syncMutedState(nextMuted: boolean, nextVolume?: number) {
    const video = mediaElement();
    const effectiveVolume = nextVolume ?? volume;
    if (player) {
      player.muted = nextMuted;
      if (nextVolume !== undefined) player.volume = effectiveVolume;
      if (nextMuted) player.setAttribute("muted", "");
      else player.removeAttribute("muted");
    }
    if (video) {
      video.muted = nextMuted;
      video.defaultMuted = nextMuted;
      if (nextVolume !== undefined) video.volume = effectiveVolume;
      if (nextMuted) video.setAttribute("muted", "");
      else video.removeAttribute("muted");
    }
    muted = nextMuted;
    if (nextVolume !== undefined) {
      volume = effectiveVolume;
      if (effectiveVolume > 0) lastNonZeroVolume = effectiveVolume;
    } else {
      volume = player?.volume ?? video?.volume ?? volume;
      if (volume > 0) lastNonZeroVolume = volume;
    }
  }

  function applyInitialMuted() {
    if (!initialMuted || muteTouched) return;
    syncMutedState(true, 0);
  }

  function scheduleInitialMutedSync() {
    if (!initialMuted || muteTouched) return;
    clearInitialMutedSync();
    let attempts = 0;
    const sync = () => {
      if (muteTouched) {
        initialMutedSyncTimer = null;
        return;
      }
      syncVideoElement();
      applyInitialMuted();
      attempts += 1;
      if (attempts < 16) {
        initialMutedSyncTimer = window.setTimeout(sync, 75);
      } else {
        initialMutedSyncTimer = null;
      }
    };
    sync();
  }

  async function playWithFallback() {
    if (!player) return;
    // A cold source (on-demand remux still producing its first segment) has no renderable data yet,
    // so 'canplay'/'playing' won't fire for a while and 'waiting' never fires from a standing start.
    // Surface the loading state immediately on the play intent so the control shows a spinner instead
    // of a dead pause icon over a black frame (otherwise it looks like the click did nothing and users
    // mash the button). 'canplay'/'playing'/'seeked' all clear it once data arrives.
    if ((mediaElement()?.readyState ?? 0) < 3 /* HAVE_FUTURE_DATA */) {
      buffering = true;
    }
    try {
      await player.play();
      const video = mediaElement();
      if (video?.paused) {
        await video.play();
      }
      playRetried = false;
    } catch {
      // A play() rejection means the media isn't ready yet ("media not ready"): a direct file still
      // loading its first frames, an on-demand remux producing its first segment, or a load in flight.
      // Do NOT fall back or force a transcode here — that would cancel a remux before it serves a
      // segment (the "play, fail, play again" symptom). Keep the play INTENT alive instead of a single
      // short retry that gives up: defer to 'canplay' so playback starts the moment data arrives,
      // however long that takes, so one press always plays. A short retry also covers the case where
      // the media became ready between the failure and now (so 'canplay' will not fire again). A
      // genuinely undecodable stream surfaces as a media 'error' event, where the decode-gated
      // fallback lives.
      buffering = true;
      pendingAutoPlay = true;
      if (!playRetried) {
        playRetried = true;
        window.setTimeout(() => {
          if (player && (mediaElement()?.paused ?? player.paused)) {
            void playWithFallback();
          }
        }, 400);
      }
    }
  }

  function applyPlaybackFallback(): boolean {
    const nextMode = fallbackPlaybackModeForError({
      effectiveMode,
      hlsSrc: effectiveHlsSrc,
      directSrc,
      directPlayable: directAvailable,
      directFailed: failedDirectSrc === directSrc,
    });
    if (!nextMode) return false;

    pendingSeekTime = currentTime > 0.25 ? currentTime : null;
    pendingAutoPlay = playing || autoPlay || pendingAutoPlay;
    if (effectiveMode === "direct" && directSrc) {
      failedDirectSrc = directSrc;
      playerNotice = "Direct playback unavailable; trying adaptive HLS.";
    } else {
      playerNotice = "Adaptive playback unavailable; trying direct playback.";
    }
    playbackMode = nextMode;
    qualityMode = nextMode === "direct" ? "direct" : "auto";
    buffering = true;
    return true;
  }

  /**
   * Final recovery tier: when direct↔HLS fallback can no longer help (e.g. the server
   * remuxed a stream this browser can't actually decode, so both the direct file and the
   * copy fail), ask the host to re-negotiate a guaranteed-playable transcode and swap to it
   * in place, preserving position. Returns true when an attempt was kicked off so the caller
   * suppresses the terminal error notice. Runs at most once per source.
   */
  function tryForceTranscodeFallback(): boolean {
    if (!onForceTranscode || forceTranscodeRequested) return false;
    forceTranscodeRequested = true;
    pendingSeekTime = currentTime > 0.25 ? currentTime : null;
    pendingAutoPlay = playing || autoPlay || pendingAutoPlay;
    buffering = true;
    playerNotice = "Switching to a compatible stream…";
    void (async () => {
      let url: string | null = null;
      try {
        url = await onForceTranscode(pendingSeekTime ?? 0);
      } catch {
        url = null;
      }
      if (url && url !== effectiveHlsSrc) {
        // Block direct play and pin to the forced transcode; the source effect reloads it.
        failedDirectSrc = directSrc ?? null;
        forcedTranscodeSrc = url;
        playbackMode = "hls";
        qualityMode = "auto";
      } else {
        playerNotice = "Playback error: no compatible stream is available.";
        buffering = false;
      }
    })();
    return true;
  }

  function seek(delta: number) {
    seekTo(currentTime + delta);
  }

  function seekBy(delta: number) {
    seek(delta);
  }

  function seekTo(time: number) {
    if (!player) return;
    const target = Math.max(0, Math.min(duration || time, time));
    player.currentTime = target;
    const video = mediaElement();
    if (video && Math.abs(video.currentTime - target) > 0.15) {
      video.currentTime = target;
    }
    currentTime = target;
    onTimeUpdate?.(target);
    updateBuffered();
  }

  function toggleMute() {
    muteTouched = true;
    clearInitialMutedSync();
    const video = mediaElement();
    const nextMuted = !(player?.muted || video?.muted || muted);
    syncMutedState(nextMuted, nextMuted ? 0 : lastNonZeroVolume || 1);
  }

  function handleVolumeChange(next: number) {
    muteTouched = true;
    clearInitialMutedSync();
    syncMutedState(next === 0, next);
  }

  function applyPlaybackRate(nextRate: number) {
    if (!player) return;
    player.playbackRate = nextRate;
    playbackRate = nextRate;
    closeMenus();
  }

  function castWindow(): CastWindow | null {
    if (typeof window === "undefined") return null;
    return window as CastWindow;
  }

  function isGoogleCastFrameworkAvailable(): boolean {
    const target = castWindow();
    return Boolean(googleCastFrameworkAvailable || target?.chrome?.cast?.isAvailable);
  }

  function loadGoogleCastFramework(): Promise<boolean> {
    const target = castWindow();
    if (!target || typeof document === "undefined") return Promise.resolve(false);
    if (isGoogleCastFrameworkAvailable()) {
      googleCastFrameworkAvailable = true;
      return Promise.resolve(true);
    }
    if (googleCastFrameworkPromise) return googleCastFrameworkPromise;

    googleCastFrameworkPromise = new Promise<boolean>((resolve) => {
      let settled = false;
      let timeoutId: number;
      const finish = (available: boolean) => {
        if (settled) return;
        settled = true;
        window.clearTimeout(timeoutId);
        googleCastFrameworkAvailable = available;
        resolve(available);
      };
      const previousCallback = target.__onGCastApiAvailable;
      target.__onGCastApiAvailable = (available: boolean) => {
        previousCallback?.(available);
        finish(Boolean(available));
      };
      timeoutId = window.setTimeout(
        () => finish(Boolean(target.chrome?.cast?.isAvailable)),
        10000,
      );
      let script = document.querySelector<HTMLScriptElement>(
        `script[src="${GOOGLE_CAST_SENDER_URL}"]`,
      );
      if (!script) {
        script = document.createElement("script");
        script.src = GOOGLE_CAST_SENDER_URL;
        script.async = true;
        document.head.appendChild(script);
      }
      script.addEventListener("error", () => finish(false), { once: true });
      script.addEventListener(
        "load",
        () => {
          if (target.chrome?.cast?.isAvailable) finish(true);
        },
        { once: true },
      );
    }).finally(() => {
      googleCastFrameworkPromise = null;
    });

    return googleCastFrameworkPromise;
  }

  function animateControlPress(event: MouseEvent) {
    const target = event.currentTarget;
    if (!(target instanceof HTMLElement)) return;
    target.classList.remove("is-pressed");
    void target.offsetWidth;
    target.classList.add("is-pressed");
    window.setTimeout(() => target.classList.remove("is-pressed"), 180);
  }

  async function requestCast(event: MouseEvent) {
    if (!player) return;
    const remote = (player as unknown as {
      remoteControl?: {
        requestAirPlay?: (trigger?: Event) => void;
        requestGoogleCast?: (trigger?: Event) => void;
      };
    }).remoteControl;
    if (!remote?.requestAirPlay && !remote?.requestGoogleCast) {
      playerNotice = "Casting is not available for this player.";
      return;
    }
    if ("WebKitPlaybackTargetAvailabilityEvent" in window) {
      if (remote.requestAirPlay) {
        playerNotice = null;
        remote.requestAirPlay(event);
        return;
      }
      playerNotice = "AirPlay is not available for this player.";
      return;
    }

    if (!remote.requestGoogleCast) {
      playerNotice = "Google Cast is not available for this player.";
      return;
    }

    if (!(await loadGoogleCastFramework())) {
      playerNotice = "Google Cast is not available for this browser.";
      return;
    }
    try {
      remote.requestGoogleCast(event);
      playerNotice = null;
      return;
    } catch (error) {
      console.error("ERROR MediaPlayer [vidstack] Google Cast request failed", error);
      playerNotice = "Google Cast is not available for this browser.";
    }
  }

  function openSettings(view: SettingsView = "root") {
    clearSettingsCloseTimer();
    settingsView = view;
    settingsMenuRendered = true;
    settingsMenuClosing = false;
  }

  function closeSettings() {
    if (!settingsMenuRendered || settingsMenuClosing) return;
    clearSettingsCloseTimer();
    settingsMenuClosing = true;
    settingsCloseTimer = window.setTimeout(() => {
      settingsMenuRendered = false;
      settingsMenuClosing = false;
      settingsView = "root";
      settingsCloseTimer = null;
    }, 180);
  }

  function toggleSettings() {
    if (settingsMenuRendered && !settingsMenuClosing) {
      closeSettings();
      return;
    }
    openSettings();
  }

  async function toggleFullscreen() {
    if (isDocumentFullscreen()) {
      exitDocumentFullscreen();
      return;
    }
    if (!containerEl) return;
    const entered = await enterMediaFullscreen(containerEl, videoEl);
    playerNotice = entered ? null : "Fullscreen is not available for this browser.";
  }

  function updateTimelineHover(clientX: number, rect: DOMRect) {
    if (duration <= 0) {
      timelineHover = null;
      return;
    }
    const percent = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    const time = percent * duration;
    const windowSec = Math.max(duration * 0.01, 1.5);
    const markerTitles = markers
      .filter((marker) => Math.abs(marker.time - time) <= windowSec)
      .map((marker) => marker.title);
    const chapterTitle = findTimelineChapterTitle(markerChapterCues, time);
    timelineHover = { chapterTitle, markerTitles, percent: percent * 100, time };
  }

  function removeMarkerChapterTrack(target: MediaPlayerElement) {
    const existing = markerChaptersTrack ?? target.textTracks.getById(MARKER_CHAPTERS_TRACK_ID);
    if (existing) target.textTracks.remove(existing);
    markerChaptersTrack = null;
  }

  function setMarkerChapterTrack(target: MediaPlayerElement, cues: readonly VTTCueInit[]) {
    removeMarkerChapterTrack(target);
    if (cues.length === 0) return;

    const track = new TextTrack({
      id: MARKER_CHAPTERS_TRACK_ID,
      kind: "chapters",
      label: "Markers",
      default: true,
    });
    for (const cue of cues) {
      track.addCue(new window.VTTCue(cue.startTime, cue.endTime, cue.text));
    }
    target.textTracks.add(track);
    markerChaptersTrack = track;
  }

  function syncMarkerChapterTrack() {
    if (!player?.textTracks) return;
    setMarkerChapterTrack(player, markerChapterCues);
  }

  function handleFilmStripInteraction(active: boolean) {
    if (active) {
      clearControlsTimer();
      showControls = false;
      closeMenus();
    }
  }

  function wait(ms: number) {
    return new Promise((resolve) => window.setTimeout(resolve, ms));
  }

  function updateActiveCue() {
    if (!activeSubtitleId || activeTrackCues.length === 0) {
      if (activeCueText !== null) {
        activeCueText = null;
        onActiveCueChange?.(null);
      }
      return;
    }
    const cue = activeTrackCues.find((candidate) => (
      currentTime >= candidate.start && currentTime < candidate.end
    ));
    const text = cue?.text.replace(/<[^>]+>/g, "") || null;
    if (text === activeCueText) return;
    activeCueText = text;
    onActiveCueChange?.(cue && text ? { start: cue.start, end: cue.end, text } : null);
  }

  $effect(() => {
    handle = {
      seekTo,
      seekBy,
      toggleMute,
      togglePlay,
    };
  });

  $effect(() => {
    const nextKey = `${src ?? ""}|${directSrc ?? ""}|${defaultPlaybackMode ?? ""}|${directPlayable ? "direct" : "adaptive"}`;
    if (nextKey === lastSourceKey) return;
    lastSourceKey = nextKey;
    failedDirectSrc = null;
    forcedTranscodeSrc = null;
    forceTranscodeRequested = false;
    playRetried = false;
    // Re-apply this device's saved quality choice so a pinned cap follows the viewer across videos.
    const sourcePolicy = resolveInitialVideoPlayerSourcePolicy({
      src,
      directSrc,
      defaultPlaybackMode,
      directAvailable,
      savedQuality: readQualityPreference(),
      qualityRungs,
    });
    playbackMode = sourcePolicy.playbackMode;
    qualityMode = sourcePolicy.qualityMode;
    selectedRungName = sourcePolicy.selectedRungName;
    currentTime = 0;
    duration = propDuration ?? 0;
    bufferAhead = 0;
    playerNotice = null;
    activeQualityLabel = null;
    playTracked = false;
    endedTracked = false;
    muteTouched = false;
    lastNonZeroVolume = 1;
    autoSelected = false;
    scheduleInitialMutedSync();
  });

  $effect(() => {
    const nextSrc = requestedPlayerSrc;
    if (!nextSrc) {
      hlsReadySrc = undefined;
      return;
    }

    const statusUrl = effectiveMode === "hls" ? hlsStatusUrlForSrc(nextSrc) : null;
    if (!statusUrl) {
      hlsReadySrc = nextSrc;
      return;
    }

    let cancelled = false;
    hlsReadySrc = undefined;
    buffering = true;
    playerNotice = "Preparing adaptive stream...";

    const poll = async () => {
      while (!cancelled) {
        try {
          const response = await fetch(statusUrl, { cache: "no-store" });
          if (!response.ok) {
            throw new Error(`HLS status failed (${response.status})`);
          }
          const status = (await response.json()) as HlsStatus;
          if (status.state === "ready") {
            if (!cancelled) {
              hlsReadySrc = nextSrc;
              playerNotice = null;
            }
            return;
          }
          if (status.state === "error") {
            throw new Error(status.error ?? "HLS generation failed");
          }
        } catch (error) {
          if (!cancelled) {
            playerNotice = error instanceof Error ? error.message : String(error);
            buffering = false;
          }
          return;
        }
        await wait(HLS_RETRY_AFTER_SECONDS * 1000);
      }
    };

    void poll();

    return () => {
      cancelled = true;
    };
  });

  $effect(() => {
    if (propDuration && propDuration > duration) duration = propDuration;
  });

  $effect(() => {
    const el = player;
    const cues = markerChapterCues;
    if (!el?.textTracks) return;

    setMarkerChapterTrack(el, cues);
    return () => removeMarkerChapterTrack(el);
  });

  $effect(() => {
    const playlist = trickplayPlaylist;
    timelineTrickplayFrames = null;
    timelineTrickplayError = false;
    if (!playlist) return;

    let cancelled = false;
    loadTrickplayFrames(playlist)
      .then((frames) => {
        if (!cancelled) timelineTrickplayFrames = frames;
      })
      .catch(() => {
        if (!cancelled) timelineTrickplayError = true;
      });

    return () => {
      cancelled = true;
    };
  });

  $effect(() => {
    const nextAutoSelectionKey = [
      subtitleDefaults?.autoEnable ? "auto" : "manual",
      subtitleDefaults?.preferredLanguages ?? "",
      subtitleTracks
        .map((track) => `${track.id}:${track.language}:${track.label ?? ""}`)
        .join("|"),
    ].join("::");
    if (nextAutoSelectionKey !== autoSelectionKey) {
      autoSelectionKey = nextAutoSelectionKey;
      autoSelected = false;
    }

    if (autoSelected) return;
    if (subtitleChoiceLocked) return;
    if (controlledSubtitleId !== undefined && controlledSubtitleId !== null) return;
    if (!subtitleDefaults?.autoEnable || subtitleTracks.length === 0) return;
    const picked = pickPreferredSubtitleTrack(
      subtitleTracks.map((track) => ({
        id: track.id,
        language: track.language,
        label: track.label,
        isDefault: track.isDefault,
      })),
      subtitleDefaults.preferredLanguages,
    );
    if (picked) {
      autoSelected = true;
      selectSubtitle(picked);
    }
  });

  $effect(() => {
    if (!activeSubtitleId) {
      activeTrackCues = [];
      activeCueText = null;
      onActiveCueChange?.(null);
      return;
    }
    const track = subtitleTracks.find((candidate) => candidate.id === activeSubtitleId);
    if (!track || isAssTrackActive(track.id, subtitleTracks)) {
      activeTrackCues = [];
      activeCueText = null;
      onActiveCueChange?.(null);
      return;
    }

    let cancelled = false;
    fetchVideoSubtitleCues(track)
      .then(({ cues }) => {
        if (!cancelled) activeTrackCues = cues;
      })
      .catch(() => {
        if (!cancelled) activeTrackCues = [];
      });
    return () => {
      cancelled = true;
    };
  });

  $effect(() => {
    currentTime;
    activeTrackCues;
    activeSubtitleId;
    updateActiveCue();
  });

  $effect(() => {
    const el = player;
    if (!el) return;
    scheduleInitialMutedSync();

    const listeners: Array<[string, EventListener]> = [
      ["provider-change", handleProviderChange],
      ["can-play", handleCanPlay],
      ["time-update", handleTimeUpdate],
      ["play", handlePlay],
      ["playing", handlePlaying],
      ["pause", handlePause],
      ["ended", handleEnded],
      ["waiting", handleWaiting],
      ["seeking", handleWaiting],
      ["seeked", handleSeeked],
      ["volume-change", handleVolumeChangeEvent],
      ["rate-change", handleRateChange],
      ["progress", handleProgress],
      ["audio-tracks-change", handleAudioTracksChange],
      ["audio-track-change", handleAudioTrackChange],
      ["qualities-change", handleQualitiesChange],
      ["quality-change", handleQualityChange],
      ["error", handleError],
    ];

    for (const [type, listener] of listeners) el.addEventListener(type, listener);
    return () => {
      for (const [type, listener] of listeners) el.removeEventListener(type, listener);
    };
  });

  $effect(() => {
    const video = videoEl;
    if (!video) return;
    scheduleInitialMutedSync();
    const onProgress = () => updateBuffered();
    const onNativeTimeUpdate = () => {
      currentTime = video.currentTime;
      onTimeUpdate?.(video.currentTime);
      updateBuffered();
    };
    const onLoadedMetadata = () => {
      duration = Math.max(video.duration || 0, propDuration ?? 0);
      updateBuffered();
    };
    const onNativePlay = () => {
      playing = true;
      endedTracked = false;
      if (!playTracked) {
        playTracked = true;
        onPlayStarted?.();
      }
      scheduleControlsHide();
    };
    const onNativePlaying = () => {
      buffering = false;
      playing = true;
      scheduleControlsHide();
    };
    const onNativePause = () => {
      playing = false;
      showControls = true;
      clearControlsTimer();
    };
    const onNativeEnded = () => {
      playing = false;
      showControls = true;
      clearControlsTimer();
      notifyPlaybackEnded();
    };
    const onNativeWaiting = () => {
      buffering = true;
    };
    const onNativeSeeked = () => {
      buffering = false;
      playing = !video.paused;
      updateBuffered();
    };
    const onNativeVolumeChange = () => {
      muted = video.muted || video.volume === 0;
      volume = video.volume;
    };
    const onNativeRateChange = () => {
      playbackRate = video.playbackRate;
    };
    video.addEventListener("timeupdate", onNativeTimeUpdate);
    video.addEventListener("progress", onProgress);
    video.addEventListener("loadedmetadata", onLoadedMetadata);
    video.addEventListener("play", onNativePlay);
    video.addEventListener("playing", onNativePlaying);
    video.addEventListener("pause", onNativePause);
    video.addEventListener("ended", onNativeEnded);
    video.addEventListener("waiting", onNativeWaiting);
    video.addEventListener("seeking", onNativeWaiting);
    video.addEventListener("seeked", onNativeSeeked);
    video.addEventListener("volumechange", onNativeVolumeChange);
    video.addEventListener("ratechange", onNativeRateChange);
    return () => {
      video.removeEventListener("timeupdate", onNativeTimeUpdate);
      video.removeEventListener("progress", onProgress);
      video.removeEventListener("loadedmetadata", onLoadedMetadata);
      video.removeEventListener("play", onNativePlay);
      video.removeEventListener("playing", onNativePlaying);
      video.removeEventListener("pause", onNativePause);
      video.removeEventListener("ended", onNativeEnded);
      video.removeEventListener("waiting", onNativeWaiting);
      video.removeEventListener("seeking", onNativeWaiting);
      video.removeEventListener("seeked", onNativeSeeked);
      video.removeEventListener("volumechange", onNativeVolumeChange);
      video.removeEventListener("ratechange", onNativeRateChange);
    };
  });

  onMount(() => {
    mediaMounted = true;
    directCapabilityProbe = document.createElement("video");
    localAppearance = readLocalSubtitleAppearance();
    syncVideoElement();
    scheduleInitialMutedSync();
    if (showCastControls && fullChrome) void loadGoogleCastFramework();

    const handleKey = (event: KeyboardEvent) => {
      if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) return;
      switch (event.key.toLowerCase()) {
        case " ":
        case "k":
          if (event.key.toLowerCase() === "k" && (event.metaKey || event.ctrlKey)) break;
          event.preventDefault();
          togglePlay();
          break;
        case "arrowleft":
          seek(-5);
          break;
        case "arrowright":
          seek(5);
          break;
        case "j":
          seek(-10);
          break;
        case "l":
          seek(10);
          break;
        case "m":
          toggleMute();
          break;
        case "f":
          void toggleFullscreen();
          break;
      }
    };

    if (enableKeyboardShortcuts) {
      window.addEventListener("keydown", handleKey);
    }
    return () => {
      clearControlsTimer();
      clearSettingsCloseTimer();
      clearInitialMutedSync();
      if (enableKeyboardShortcuts) {
        window.removeEventListener("keydown", handleKey);
      }
      onActiveCueChange?.(null);
    };
  });

  function handleProviderChange(event: Event) {
    const provider = (event as MediaProviderChangeEvent).detail;
    if (isHLSProvider(provider)) {
      provider.config = {
        ...adaptiveHlsBufferConfig(),
      };
    }
    syncVideoElement();
    scheduleInitialMutedSync();
  }

  function handleCanPlay(event: Event) {
    const detail = (event as MediaCanPlayEvent).detail;
    duration = Math.max(detail.duration || 0, propDuration ?? 0);
    buffering = false;
    syncVideoElement();
    scheduleInitialMutedSync();
    refreshQualities();
    refreshAudioTracks();
    syncMarkerChapterTrack();
    updateBuffered();
    onCanPlay?.();
    if (pendingSeekTime !== null) {
      player!.currentTime = Math.min(duration || pendingSeekTime, pendingSeekTime);
      pendingSeekTime = null;
    }
    if ((pendingAutoPlay || autoPlay) && player?.paused) {
      pendingAutoPlay = false;
      void playWithFallback();
    }
  }

  function handleTimeUpdate(event: Event) {
    const detail = (event as MediaTimeUpdateEvent).detail;
    currentTime = detail.currentTime;
    onTimeUpdate?.(detail.currentTime);
    updateBuffered();
  }

  function handlePlay(_event: Event) {
    playing = true;
    endedTracked = false;
    // Mirror playWithFallback: if playback starts before there is renderable data (cold remux),
    // keep the loading spinner up rather than flashing to a pause icon over a black frame.
    if ((mediaElement()?.readyState ?? 0) < 3 /* HAVE_FUTURE_DATA */) {
      buffering = true;
    }
    if (!playTracked) {
      playTracked = true;
      onPlayStarted?.();
    }
    scheduleControlsHide();
  }

  function handlePlaying(_event: Event) {
    buffering = false;
    playing = true;
    scheduleControlsHide();
  }

  function handlePause(_event: Event) {
    playing = false;
    showControls = true;
    clearControlsTimer();
  }

  function handleEnded(_event: Event) {
    playing = false;
    showControls = true;
    clearControlsTimer();
    notifyPlaybackEnded();
  }

  function handleWaiting(_event: Event) {
    buffering = true;
  }

  function handleSeeked(_event: Event) {
    buffering = false;
    playing = player ? !player.paused : false;
  }

  function handleVolumeChangeEvent(_event: Event) {
    const video = mediaElement();
    muted = Boolean(player?.muted || video?.muted || player?.volume === 0 || video?.volume === 0);
    volume = player?.volume ?? video?.volume ?? volume;
    if (volume > 0) lastNonZeroVolume = volume;
  }

  function handleRateChange(_event: Event) {
    if (player) playbackRate = player.playbackRate;
  }

  function handleProgress(_event: Event) {
    updateBuffered();
  }

  function handleAudioTracksChange(_event: Event) {
    refreshAudioTracks();
  }

  function handleAudioTrackChange(_event: Event) {
    refreshAudioTracks();
  }

  function handleQualitiesChange(_event: Event) {
    refreshQualities();
  }

  function handleQualityChange(_event: Event) {
    refreshQualities();
  }

  // True only for a genuine, unrecoverable codec/decode failure (the browser cannot play this
  // stream at all) — MediaError DECODE (3) or SRC_NOT_SUPPORTED (4), or an equivalent message.
  // Network/abort/transient errors return false: those recover on their own and must NOT trigger a
  // transcode fallback, or a perfectly playable remux gets abandoned for a heavy re-encode.
  function isFatalDecodeError(detail: unknown): boolean {
    const d = detail as { code?: number; mediaError?: { code?: number }; message?: string } | null;
    const code = d?.code ?? d?.mediaError?.code;
    if (code === 3 || code === 4) return true;
    const message = (d?.message ?? "").toLowerCase();
    return message.includes("decode") ||
      message.includes("not supported") ||
      message.includes("buffer append") ||
      message.includes("src_not_supported");
  }

  function handleError(event: Event) {
    const detail = (event as MediaErrorEvent).detail;
    const message = detail instanceof Error
      ? detail.message
      : (detail as { message?: string } | null)?.message ?? "Playback failed.";
    if (applyPlaybackFallback()) return;
    // Only escalate to a re-negotiated transcode when the browser genuinely cannot decode the
    // stream. A transient/network error here would otherwise tear down a working remux.
    if (isFatalDecodeError(detail) && tryForceTranscodeFallback()) return;
    playerNotice = `${effectiveMode === "direct" ? "Direct" : "Adaptive"} playback error: ${message}`;
    buffering = false;
  }
</script>

<div class="space-y-1" data-testid="vidstack-video-player">
  <!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
  <div
    bind:this={containerEl}
    class="prismedia-player-surface relative surface-media-well bg-black"
    onmousemove={surfaceControls}
    onclickcapture={handleMinimalSurfaceClick}
    onmouseleave={() => {
      if (playing) showControls = false;
    }}
    ontouchstart={surfaceControls}
  >
    {#if playerSrc && mediaMounted}
      <!-- svelte-ignore a11y_click_events_have_key_events -->
      <media-player
        class="prismedia-media-engine"
        title={mediaTitle || "Prismedia video"}
        artist={mediaArtist || undefined}
        src={playerSrc}
        poster={poster}
        streamType="on-demand"
        crossOrigin
        playsInline
        load={autoPlay || pendingAutoPlay || playing ? "eager" : "play"}
        posterLoad="eager"
        autoPlay={autoPlay}
        loop={autoRepeat}
        muted={initialMuted}
        bind:this={player}
        onclick={(event) => {
          if (!fullChrome) {
            event.preventDefault();
            event.stopPropagation();
            event.stopImmediatePropagation();
            togglePlay();
            return;
          }
          if (event.target === event.currentTarget || event.target instanceof HTMLVideoElement) {
            togglePlay();
          }
        }}
      >
        <media-provider>
          {#if poster}
            <media-poster class="vds-poster" src={poster} alt="Video poster"></media-poster>
          {/if}
        </media-provider>
        <VideoTimeline
          {bufferedProgressPercent}
          {fullChrome}
          markersCount={markers.length}
          onHover={updateTimelineHover}
          onHoverEnd={() => (timelineHover = null)}
          {playbackProgressPercent}
          {showControls}
          {timelineHover}
          {timelinePreviewFrame}
          {timelinePreviewSpriteDims}
        />
      </media-player>
    {:else if requestedPlayerSrc}
      <div class="prismedia-media-engine flex items-center justify-center">
        <Loader class="h-5 w-5 animate-spin text-white/40" />
      </div>
    {:else}
      <div class="flex aspect-video items-center justify-center bg-surface-1">
        <div class="text-center">
          <Play class="mx-auto mb-3 h-16 w-16 text-text-disabled" />
          <p class="text-sm text-text-muted">No video source</p>
          <p class="mt-1 text-xs text-text-disabled">Video playback will appear here</p>
        </div>
      </div>
    {/if}

    <VideoSubtitleOverlay
      {activeCueText}
      {appearance}
      assTrack={assTrackForRender}
      showTextCue={showTextSubtitleCue}
      videoEl={videoEl ?? null}
    />

    {#if fullChrome}
      <VideoStatusBar
        audioFormatLabel={activeAudioFormatLabel}
        bufferSeconds={bufferAhead}
        dynamicRangeLabel={dynamicRangeLabel}
        methodDetail={playbackMethodDetail}
        methodHint={playbackMethodHint}
        methodLabel={playbackMethodLabel}
        playbackMethod={playbackMethod}
        {playerNotice}
        resolutionLabel={resolutionBadgeLabel}
        {showControls}
        videoDetail={videoBadgeDetail}
      />

      <div
        class={cn(
          "pointer-events-none absolute inset-0 z-30 flex items-center justify-center transition-opacity duration-normal sm:hidden",
          showControls ? "opacity-100" : "opacity-0",
        )}
      >
      <VideoTransportControls
        variant="mobile"
        {buffering}
        {playing}
        onSeek={seek}
        onTogglePlay={togglePlay}
      />
      </div>

      <div
        class={cn(
          "player-control-bar pointer-events-none absolute inset-x-0 bottom-0 z-20 pb-5 pt-8 transition-opacity duration-normal sm:pb-4 sm:pt-20",
          showControls ? "opacity-100" : "opacity-0",
        )}
      >
      <div class="flex flex-col gap-2">
        {#if markers.length > 0}
          <div class="pointer-events-auto order-3 hidden flex-wrap gap-1.5 sm:order-1 sm:flex">
            {#each markers as marker (marker.id)}
              <button
                type="button"
                data-testid="video-marker-chip"
                onpointerdown={(event) => event.stopPropagation()}
                onclick={(event) => {
                  event.stopPropagation();
                  seekTo(marker.time);
                  onMarkerClick?.(marker);
                }}
                title={`Seek to ${marker.title}`}
                aria-label={`Seek to ${marker.title}`}
                class="player-chip px-2.5 py-1 text-[0.68rem] text-white/72 transition-colors hover:border-accent-400/35 hover:text-white"
              >
                {marker.title}
              </button>
            {/each}
          </div>
        {/if}

        <div class="pointer-events-auto order-1 flex flex-col gap-2 sm:order-2 sm:flex-row sm:items-center sm:justify-between">
          <div class="flex w-full items-center justify-end gap-2 sm:w-auto sm:justify-start sm:gap-2.5">
            <VideoTransportControls
              {buffering}
              {playing}
              onSeek={seek}
              onTogglePlay={togglePlay}
            />

            <div class="hidden sm:flex items-center gap-2 text-white/80">
              <button type="button" onclick={toggleMute} class="transition-colors hover:text-white" aria-label={muted ? "Unmute" : "Mute"}>
                {#if muted}<VolumeX class="h-4 w-4" />{:else}<Volume2 class="h-4 w-4" />{/if}
              </button>
              <input
                aria-label="Volume"
                type="range"
                min="0"
                max="1"
                step="0.05"
                value={muted ? 0 : volume}
                oninput={(event) => handleVolumeChange(Number(event.currentTarget.value))}
                class="prismedia-range h-1 w-20"
              />
            </div>

            <span class="shrink-0 whitespace-nowrap text-mono-tabular text-glow-phosphor text-[0.7rem] sm:text-xs">
              {formatTime(currentTime)} / {formatTime(duration)}
            </span>
          </div>

          <div class="flex w-full items-center justify-between gap-2 sm:w-auto sm:justify-start">
            <div class="flex min-w-0 shrink items-center gap-2">
              {#if subtitleTracks.length > 0 && onTranscriptSidecarToggle}
                <button
                  type="button"
                  onclick={(event) => {
                    animateControlPress(event);
                    onTranscriptSidecarToggle();
                  }}
                  aria-label={isTranscriptSidecarOpen ? "Hide transcript sidecar" : "Show transcript sidecar"}
                  title={isTranscriptSidecarOpen ? "Hide transcript sidecar" : "Show transcript sidecar"}
                  class={cn(
                    "player-control-button sidecar-control-button text-[0.56rem] sm:text-[0.72rem]",
                    isTranscriptSidecarOpen
                      ? "border-accent-500/45 bg-accent-500/12 text-accent-100 shadow-[var(--shadow-glow-accent)]"
                      : "text-white/82",
                  )}
                >
                  <PanelRightOpen class="h-3 w-3 sm:h-3.5 sm:w-3.5" />
                </button>
              {/if}
            </div>

            <div class="relative flex min-w-0 items-center justify-end gap-2">
              <button
                type="button"
                onclick={(event) => {
                  animateControlPress(event);
                  toggleSettings();
                }}
                class={cn(
                  "player-control-button justify-center p-0 text-white/80",
                  settingsMenuRendered &&
                    !settingsMenuClosing &&
                    "border-accent-500/40 text-accent-100 shadow-[var(--shadow-glow-accent)]",
                )}
                aria-label="Player settings"
                aria-expanded={settingsMenuRendered && !settingsMenuClosing}
              >
                <Settings2 class="h-3 w-3 sm:h-4 sm:w-4" />
              </button>

              {#if showCastControls}
                <button
                  type="button"
                  onclick={(event) => {
                    animateControlPress(event);
                    void requestCast(event);
                  }}
                  class="player-control-button justify-center p-0 text-white/80"
                  aria-label="Cast"
                  title="Cast"
                >
                  <Cast class="h-3 w-3 sm:h-4 sm:w-4" />
                </button>
              {/if}

              <button
                type="button"
                onclick={(event) => {
                  animateControlPress(event);
                  void toggleFullscreen();
                }}
                class="player-control-button justify-center p-0 text-white/80"
                aria-label="Fullscreen"
              >
                <Maximize class="h-3 w-3 sm:h-4 sm:w-4" />
              </button>
            </div>
          </div>
        </div>
      </div>
      </div>

      {#if settingsMenuRendered}
        <VideoSettingsMenu
          activeQualityLabel={activeQualityLabel}
          activeSubtitleId={activeSubtitleId}
          {activeSubtitleLabel}
          {appearance}
          closing={settingsMenuClosing}
          {displayedAudioTrackLabel}
          {displayedAudioTracks}
          {localAppearance}
          onAppearanceChange={handleAppearanceChange}
          onAppearanceReset={handleAppearanceReset}
          onClose={closeSettings}
          onOpenView={openSettings}
          onPlaybackRateChange={applyPlaybackRate}
          onQualityChange={requestPlaybackMode}
          onSelectAudioTrack={selectAudioTrack}
          onSelectSubtitle={selectSubtitle}
          onViewChange={(view) => (settingsView = view)}
          {playbackRate}
          {qualityMode}
          {qualityOptions}
          selectedQualityLabel={selectedQualityLabel}
          {subtitleTracks}
          view={settingsView}
        />
      {/if}
    {/if}
  </div>

  {#if fullChrome && hasFilmStrip}
    <div class="border border-border-subtle bg-black overflow-hidden">
      <FilmStrip
        playlistUrl={trickplayPlaylist!}
        videoEl={videoEl ?? null}
        currentTime={currentTime}
        {duration}
        onSeek={seekTo}
        {markers}
        onStripInteractionChange={handleFilmStripInteraction}
      />
    </div>
  {/if}
</div>

<style>
  .prismedia-player-surface {
    --player-chrome-inline-padding: 0.75rem;
    container-type: inline-size;
  }

  .prismedia-media-engine {
    -webkit-tap-highlight-color: transparent;
    aspect-ratio: 16 / 9;
    background: #000;
    color: #f2eee7;
    display: block;
    margin-inline: auto;
    max-width: calc((100dvh - 14rem) * 16 / 9);
    position: relative;
    width: 100%;
  }

  .player-control-bar {
    padding-inline: var(--player-chrome-inline-padding);
    background:
      linear-gradient(
        to top,
        rgba(7, 8, 11, 0.92) 0%,
        rgba(7, 8, 11, 0.60) 50%,
        transparent 100%
      );
  }

  @media (min-width: 640px) {
    .prismedia-player-surface {
      --player-chrome-inline-padding: 1rem;
    }
  }

  .prismedia-player-surface,
  .prismedia-media-engine,
  .prismedia-media-engine :global(media-provider),
  .prismedia-media-engine :global(video),
  .prismedia-media-engine :global(media-poster) {
    outline: none;
  }

  .prismedia-player-surface:focus,
  .prismedia-player-surface:focus-visible,
  .prismedia-media-engine:focus,
  .prismedia-media-engine:focus-visible,
  .prismedia-media-engine :global(video:focus),
  .prismedia-media-engine :global(video:focus-visible),
  .prismedia-media-engine :global(media-poster:focus),
  .prismedia-media-engine :global(media-poster:focus-visible) {
    box-shadow: none;
    outline: none;
  }

  .prismedia-media-engine :global(media-provider) {
    display: block;
    height: 100%;
    inset: 0;
    position: absolute;
    width: 100%;
  }

  .prismedia-media-engine :global(video),
  .prismedia-media-engine :global(media-poster) {
    -webkit-tap-highlight-color: transparent;
    background: #000;
    border-radius: 0;
    height: 100%;
    inset: 0;
    object-fit: contain;
    position: absolute;
    width: 100%;
  }

  .prismedia-media-engine :global(media-poster img) {
    display: block;
    height: 100%;
    object-fit: cover;
    object-position: center;
    width: 100%;
  }

  :global(.prismedia-media-engine[data-started] media-poster),
  :global(.prismedia-media-engine[data-playing] media-poster) {
    opacity: 0;
    visibility: hidden;
  }

  .prismedia-player-surface:fullscreen,
  .prismedia-player-surface:-webkit-full-screen {
    background: #000;
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100dvh;
    inset: 0;
    position: fixed;
    width: 100vw;
  }

  .prismedia-player-surface:fullscreen .prismedia-media-engine,
  .prismedia-player-surface:-webkit-full-screen .prismedia-media-engine {
    aspect-ratio: auto;
    height: 100%;
    max-width: none;
    position: relative;
    width: 100%;
  }

  .prismedia-range {
    appearance: none;
    background: rgba(255, 255, 255, 0.18);
    border-radius: var(--radius-xs);
    cursor: pointer;
    transition: background var(--duration-fast) var(--ease-default);
  }

  .prismedia-range:hover {
    background: rgba(255, 255, 255, 0.24);
  }

  .prismedia-range::-webkit-slider-thumb {
    appearance: none;
    width: 0.72rem;
    height: 0.72rem;
    border-radius: 50%;
    background: var(--color-accent-300);
    box-shadow:
      0 0 0 1px rgba(196, 154, 90, 0.35),
      0 0 10px rgba(196, 154, 90, 0.65);
  }

  .prismedia-range::-moz-range-thumb {
    width: 0.72rem;
    height: 0.72rem;
    border: 0;
    border-radius: 50%;
    background: var(--color-accent-300);
    box-shadow:
      0 0 0 1px rgba(196, 154, 90, 0.35),
      0 0 10px rgba(196, 154, 90, 0.65);
  }

  .player-control-button {
    align-items: center;
    backdrop-filter: blur(var(--glass-blur-sm));
    -webkit-backdrop-filter: blur(var(--glass-blur-sm));
    background: rgba(12, 15, 21, 0.78);
    border: 1px solid rgba(148, 158, 178, 0.14);
    border-radius: var(--radius-base);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.07),
      0 2px 10px rgba(0, 0, 0, 0.35);
    display: flex;
    height: 1.75rem;
    min-height: 1.75rem;
    min-width: 1.75rem;
    transform: translateY(0) scale(1);
    transition:
      background-color var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default),
      box-shadow var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default),
      transform var(--duration-fast) var(--ease-default);
  }

  .player-control-button:hover,
  .player-control-button:focus-visible {
    background: rgba(21, 26, 40, 0.88);
    border-color: rgba(196, 154, 90, 0.45);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.10),
      0 0 0 1px rgba(196, 154, 90, 0.20),
      0 0 20px rgba(196, 154, 90, 0.30),
      0 4px 16px rgba(0, 0, 0, 0.40);
    color: white;
  }

  .player-control-button:focus-visible {
    outline: 1px solid rgba(196, 154, 90, 0.72);
    outline-offset: 2px;
  }

  .player-control-button:active,
  .player-control-button.is-pressed {
    background: rgba(33, 39, 51, 0.98);
    border-color: rgba(196, 154, 90, 0.58);
    box-shadow:
      inset 0 0 14px rgba(196, 154, 90, 0.18),
      0 0 20px rgba(196, 154, 90, 0.34);
    transform: translateY(1px) scale(0.94);
  }

  .sidecar-control-button {
    justify-content: center;
    padding: 0;
    width: 1.75rem;
  }

  @media (min-width: 640px) {
    .player-control-button {
      height: 2.25rem;
      min-height: 2.25rem;
      min-width: 2.25rem;
    }

    .sidecar-control-button {
      padding: 0;
      width: 2.25rem;
    }

  }
</style>
