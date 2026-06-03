<script lang="ts">
  import { onMount } from "svelte";
  import { beforeNavigate, goto } from "$app/navigation";
  import { page } from "$app/state";
  import {
    Captions,
    Info,
    MapPin,
    MonitorCog,
    Play,
    SlidersHorizontal,
    Users,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchEntity, fetchEntityThumbnails } from "$lib/api/entities";
  import { fetchVideo, type VideoDetail } from "$lib/api/media";
  import { fetchSettingsValues, type LibrarySettings } from "$lib/api/settings";
  import {
    markJellyfinUserPlayedItem,
    postJellyfinSessionProgress,
    updateEntityPlayback,
    type JellyfinPlaybackInfoResponse,
  } from "$lib/api/playback";
  import {
    loadPlaybackInfo as loadPlaybackInfoRequest,
    negotiateForceTranscodeSrc,
  } from "$lib/player/playback-negotiation";
  import { durationToSeconds } from "$lib/utils/format";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { settingKeys, valuesToLibrarySettings } from "$lib/settings/app-settings";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import type { EntityDetailTag } from "$lib/entities/entity-detail";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import {
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-relationship-thumbnails";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import { extractVideoPlayerProps, getPlaybackState } from "$lib/entities/video-capabilities";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import NsfwBlur from "$lib/components/nsfw/NsfwBlur.svelte";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityMetadataUpdateRequest,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import VideoPlayer, {
    type VideoPlayerHandle,
  } from "$lib/components/VideoPlayer.svelte";
  import VideoDetailSectionContent from "./VideoDetailSectionContent.svelte";
  import VideoTranscriptPanel from "$lib/components/VideoTranscriptPanel.svelte";
  import {
    buildSubtitleDefaults,
    clampTranscriptDockPercent,
    readTranscriptDockPreferences,
    writeTranscriptDockPreference,
    writeTranscriptDockWidth,
  } from "./video-page-state";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let video = $state<VideoDetail | null>(null);
  let playbackInfo = $state<JellyfinPlaybackInfoResponse | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let librarySettings = $state<LibrarySettings | null>(null);
  let studioCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  // When this video is an episode, the series it belongs to (resolved by walking the
  // season → series parent chain) so it can be linked from the breadcrumb and the detail tab.
  let seriesCard = $state<EntityThumbnailCard | null>(null);
  let seriesRef = $state<{ id: string; title: string } | null>(null);

  let playerHandle: VideoPlayerHandle | undefined = $state();
  let currentTime = $state(0);
  let displayTime = $state(0);
  let activeSubtitleId = $state<string | null>(null);
  let selectedAudioStreamIndex = $state<number | null>(null);
  let subtitleChoiceLocked = $state(false);
  let playTracked = false;
  let resumeApplied = false;
  let playbackUpdateTimer: ReturnType<typeof setInterval> | null = null;
  let lastReportedTime = 0;
  // Tracks which video id the playback session belongs to. Used so a same-video metadata
  // refresh (e.g. onTracksChanged) does not reset playback tracking and kill the update timer.
  let trackedVideoId: string | null = null;
  let hydratedSubtitlePrefsKey = "";

  // ── Transcript dock plumbing ───────────────────────────────────────
  let userWantsDock = $state(false);
  let dockVideoPercent = $state(80);
  let isDesktopViewport = $state(false);
  let videoWrapperEl: HTMLDivElement | null = $state(null);
  let videoWrapperHeight = $state<number | null>(null);
  let isResizing = false;

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!video) return null;
    return {
      ...entityCardToDetailCard(video),
      tags: relationshipTags,
    };
  });
  const identifyAction = useIdentifyDetailAction(() => card?.entity.id, () => card?.entity.kind);
  const heroActions = $derived.by((): EntityDetailActionButton[] => identifyAction.action ? [identifyAction.action] : []);
  const videoId = $derived(video?.id ?? "");

  const playerProps = $derived.by(() => {
    if (!video) return null;
    return extractVideoPlayerProps(video.id, video.capabilities, playbackInfo, selectedAudioStreamIndex);
  });

  const primaryStudio = $derived(studioCards[0]?.entity ?? null);

  const hasCastAndCrew = $derived(
    studioCards.length > 0 || creditCards.length > 0 || seriesCard != null,
  );
  const detailSections = $derived.by((): EntityDetailSection[] => [
    {
      id: "cast-and-crew",
      label: "Cast and Crew",
      icon: Users,
      hidden: !hasCastAndCrew,
    },
    {
      id: "technical",
      label: "Technical",
      icon: MonitorCog,
      hidden: (card?.technical.length ?? 0) === 0,
    },
    {
      id: "dates",
      label: "Dates",
      hidden: (card?.dates.length ?? 0) === 0,
    },
    {
      id: "playback",
      label: "Playback",
      icon: Play,
      hidden: !playbackState,
    },
    {
      id: "source",
      label: "Source",
      hidden: (card?.sources.length ?? 0) === 0 && (card?.fingerprints.length ?? 0) === 0,
    },
    {
      id: "markers",
      label: "Markers",
      count: card?.markers.length ?? 0,
    },
    {
      id: "transcript",
      label: "Transcript",
      count: playerProps?.subtitleTracks.length ?? 0,
    },
  ]);

  const detailTabs = $derived.by((): EntityDetailTab[] => {
    if (!card) return [];
    return [
      {
        id: "details",
        label: "Details",
        icon: Info,
        sections: ["description", "playback", "tags", "cast-and-crew", "links"],
      },
      {
        id: "metadata",
        label: "Metadata",
        icon: SlidersHorizontal,
        sections: ["technical", "dates", "source"],
        layout: "grid",
      },
      {
        id: "markers",
        label: "Markers",
        icon: MapPin,
        count: card.markers.length,
        sections: ["markers"],
      },
      {
        id: "transcript",
        label: "Transcript",
        icon: Captions,
        count: playerProps?.subtitleTracks.length ?? 0,
        sections: ["transcript"],
      },
    ];
  });

  const dates = $derived.by(() => {
    if (!video) return [];
    const cap = getCapability(video.capabilities, "dates");
    return cap?.items ?? [];
  });

  const flagsNsfw = $derived.by(() => {
    if (!video) return false;
    const cap = getCapability(video.capabilities, "flags");
    return cap?.isNsfw === true;
  });

  const playbackState = $derived.by(() => {
    if (!video) return null;
    return getPlaybackState(video.capabilities);
  });

  const durationSeconds = $derived.by(() => {
    if (!video) return 0;
    return durationToSeconds(getCapability(video.capabilities, "technical")?.duration ?? null) ?? 0;
  });

  let playbackBusy = $state(false);

  /** Resumes inline playback from the stored position and brings the player into view. */
  function handleResume() {
    if (!playbackState) return;
    resumeApplied = true;
    playerHandle?.seekTo(playbackState.resumeSeconds);
    videoWrapperEl?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  /** Marks the video watched or unwatched via the shared playback capability, then refreshes. */
  async function handleToggleWatched(watched: boolean) {
    if (!video || playbackBusy) return;
    playbackBusy = true;
    try {
      await updateEntityPlayback(video.id, { completed: watched });
      await refreshVideo();
    } catch {
      // best-effort; the card reflects the last known state on failure
    } finally {
      playbackBusy = false;
    }
  }

  /**
   * Resets playback to the beginning. Reporting position 0 routes through the same start-over
   * behaviour a Jellyfin client triggers (a sub-5% progress report clears resume and completion).
   */
  async function handleStartOver() {
    if (!video || playbackBusy) return;
    playbackBusy = true;
    try {
      await updateEntityPlayback(video.id, { resumeSeconds: 0 });
      resumeApplied = true;
      playerHandle?.seekTo(0);
      await refreshVideo();
    } catch {
      // best-effort
    } finally {
      playbackBusy = false;
    }
  }

  const hasSubtitles = $derived((playerProps?.subtitleTracks.length ?? 0) > 0);
  const subtitlesEnabled = $derived(activeSubtitleId != null);
  const isTranscriptDockActive = $derived(userWantsDock && hasSubtitles && subtitlesEnabled);
  const isTranscriptDocked = $derived(
    userWantsDock && hasSubtitles && subtitlesEnabled && isDesktopViewport,
  );
  const isTranscriptInlineDocked = $derived(
    isTranscriptDockActive && !isDesktopViewport,
  );

  const subtitleDefaults = $derived(buildSubtitleDefaults(librarySettings));
  const defaultPlaybackMode = $derived<"direct" | "hls">(
    librarySettings?.defaultPlaybackMode === "hls" ? "hls" : "direct",
  );
  const showCastControls = $derived(librarySettings?.showCastControls ?? true);

  // ── Lifecycle ──────────────────────────────────────────────────────

  // Flush the latest position before any client-side navigation (leaving the
  // player or switching to another video) so resume points survive navigation.
  beforeNavigate(() => {
    flushPlaybackPosition();
  });

  onMount(() => {
    void loadVideo();
    let cancelled = false;

    const dockPrefs = readTranscriptDockPreferences(window.localStorage);
    userWantsDock = dockPrefs.docked;
    dockVideoPercent = dockPrefs.videoPercent;

    const mq = window.matchMedia("(min-width: 1024px)");
    const updateViewport = () => (isDesktopViewport = mq.matches);
    updateViewport();
    mq.addEventListener("change", updateViewport);

    void fetchSettingsValues([
      settingKeys.playbackDefaultMode,
      settingKeys.playbackShowCastControls,
      settingKeys.subtitlesAutoEnable,
      settingKeys.subtitlesPreferredLanguages,
      settingKeys.subtitlesStyle,
      settingKeys.subtitlesFontScale,
      settingKeys.subtitlesPositionPercent,
      settingKeys.subtitlesOpacity,
    ])
      .then((config) => {
        if (!cancelled) librarySettings = valuesToLibrarySettings(config.values);
      })
      .catch(() => {});

    return () => {
      cancelled = true;
      mq.removeEventListener("change", updateViewport);
      if (playbackUpdateTimer) clearInterval(playbackUpdateTimer);
    };
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadVideo();
  });

  // Reset play tracking only when the video ID actually changes. A same-video metadata
  // refresh (refreshVideo via onTracksChanged) reassigns `video` but must not tear down the
  // in-flight playback session, or progress reporting would stop after the first update.
  $effect(() => {
    const nextId = video?.id ?? null;
    if (nextId === trackedVideoId) return;
    trackedVideoId = nextId;
    playTracked = false;
    resumeApplied = false;
    selectedAudioStreamIndex = null;
    lastReportedTime = 0;
    hydratedSubtitlePrefsKey = "";
    if (playbackUpdateTimer) {
      clearInterval(playbackUpdateTimer);
      playbackUpdateTimer = null;
    }
  });

  $effect(() => {
    if (!video) return;
    return appChrome.setBreadcrumbs([
      { label: "Videos", href: "/videos" },
      ...(seriesRef ? [{ label: seriesRef.title, href: `/series/${seriesRef.id}` }] : []),
      { label: video.title },
    ]);
  });

  // Hydrate subtitle preference from localStorage.
  $effect(() => {
    if (typeof window === "undefined" || !video || !playerProps) return;
    const videoId = video.id;
    const trackIds = playerProps.subtitleTracks.map((t) => t.id).join(",");
    const hydrationKey = `${videoId}:${trackIds}`;
    if (!videoId || hydratedSubtitlePrefsKey === hydrationKey) return;
    hydratedSubtitlePrefsKey = hydrationKey;
    const saved = window.localStorage.getItem(`prismedia:subtitle-lang:${videoId}`);
    if (saved) {
      const restoredSubtitleId = saved === "__off__" ? null : saved;
      const hasSavedTrack =
        restoredSubtitleId == null ||
        playerProps.subtitleTracks.some((t) => t.id === restoredSubtitleId);
      if (hasSavedTrack) {
        activeSubtitleId = restoredSubtitleId;
        subtitleChoiceLocked = true;
        return;
      }
    }
    activeSubtitleId = null;
    subtitleChoiceLocked = false;
  });

  // Mirror video wrapper height for docked transcript.
  $effect(() => {
    if (typeof window === "undefined") return;
    const el = videoWrapperEl;
    if (!el) return;
    videoWrapperHeight = Math.round(el.getBoundingClientRect().height);
    void isTranscriptDocked;
    if (typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (!entry) return;
      const next = Math.round(entry.contentRect.height);
      if (videoWrapperHeight !== next) videoWrapperHeight = next;
    });
    observer.observe(el);
    return () => observer.disconnect();
  });

  // ── Data loading ───────────────────────────────────────────────────

  async function loadVideo() {
    loadState = "loading";
    errorMessage = null;
    try {
      const nextVideo = await fetchVideo(page.params.id ?? "");
      if (await redirectMovieChildVideo(nextVideo)) return;
      video = nextVideo;
      const [nextPlaybackInfo] = await Promise.all([
        loadPlaybackInfo(nextVideo.id),
        hydrateVideoRelationships(nextVideo),
      ]);
      playbackInfo = nextPlaybackInfo;
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function refreshVideo() {
    try {
      const nextVideo = await fetchVideo(video?.id ?? page.params.id ?? "");
      video = nextVideo;
      const [nextPlaybackInfo] = await Promise.all([
        loadPlaybackInfo(nextVideo.id, playbackInfo?.PlaySessionId, selectedAudioStreamIndex),
        hydrateVideoRelationships(nextVideo),
      ]);
      playbackInfo = nextVideo
        ? nextPlaybackInfo
        : null;
    } catch {
      // best-effort
    }
  }

  async function hydrateVideoRelationships(nextVideo: VideoDetail) {
    const [relationships] = await Promise.all([
      hydrateStandardRelationshipCards(nextVideo),
      resolveSeries(nextVideo),
    ]);
    studioCards = relationships.studioCards;
    creditCards = relationships.creditCards;
    relationshipTags = relationships.relationshipTags;
  }

  /**
   * Resolves the series an episode belongs to by walking the parent chain
   * (episode → season → series), so the video can link back to its series. A standalone
   * video (or a movie child, which redirects earlier) leaves the series unset.
   */
  async function resolveSeries(nextVideo: VideoDetail) {
    seriesCard = null;
    seriesRef = null;
    const seasonId = nextVideo.parentEntityId;
    if (!seasonId) return;
    const [season] = await fetchEntityThumbnails([seasonId]).catch(() => []);
    if (season?.kind !== ENTITY_KIND.videoSeason || !season.parentEntityId) return;
    const [series] = await fetchEntityThumbnails([season.parentEntityId]).catch(() => []);
    if (series?.kind !== ENTITY_KIND.videoSeries) return;
    const href = resolveEntityHref(series.kind, series.id);
    seriesRef = { id: series.id, title: series.title };
    [seriesCard] = thumbnailsToCards([series], { hrefFor: () => href });
  }

  async function redirectMovieChildVideo(nextVideo: VideoDetail) {
    if (!nextVideo.parentEntityId) return false;

    const parent = await fetchEntity(nextVideo.parentEntityId).catch(() => null);
    if (parent?.kind !== ENTITY_KIND.movie) return false;

    const movieHref = resolveEntityHref(ENTITY_KIND.movie, parent.id);
    if (!movieHref) return false;

    await goto(movieHref, { replaceState: true });
    return true;
  }

  async function loadPlaybackInfo(
    videoId: string,
    playSessionId?: string | null,
    audioStreamIndex?: number | null,
  ) {
    return await loadPlaybackInfoRequest(videoId, {
      playSessionId,
      audioStreamIndex,
    });
  }

  // Player callback: re-negotiate a guaranteed-playable transcode after a fatal decode error.
  async function forceTranscodeFallback(): Promise<string | null> {
    if (!video) return null;
    return negotiateForceTranscodeSrc(video, selectedAudioStreamIndex, playbackInfo?.PlaySessionId);
  }

  // ── Player event handlers ──────────────────────────────────────────

  function handleTimeUpdate(t: number) {
    currentTime = t;
    displayTime = t;

    if (
      !resumeApplied &&
      video &&
      playbackState &&
      !playbackState.completedAt &&
      playbackState.resumeSeconds > 5
    ) {
      resumeApplied = true;
      playerHandle?.seekTo(playbackState.resumeSeconds);
    }
  }

  /**
   * Reports the current playback position to the backend so it can store a resume
   * point (or derive completion when near the end). Used on pause-adjacent teardown
   * and SPA navigation, complementing the periodic in-stream progress reports.
   */
  function flushPlaybackPosition() {
    if (!playTracked || !video || !playerProps || currentTime <= 0) return;
    void postJellyfinSessionProgress("Playing/Stopped", {
      ItemId: video.id,
      MediaSourceId: playerProps.mediaSourceId,
      PlaySessionId: playerProps.playSessionId,
      PositionTicks: Math.round(currentTime * 10_000_000),
    }).catch(() => {});
  }

  async function handlePlayStarted() {
    if (playTracked || !video || !playerProps) return;
    playTracked = true;

    try {
      await postJellyfinSessionProgress("Playing", {
        ItemId: video.id,
        MediaSourceId: playerProps.mediaSourceId,
        PlaySessionId: playerProps.playSessionId,
        PositionTicks: Math.round(currentTime * 10_000_000),
      });
    } catch {
      // best-effort
    }

    if (!playbackUpdateTimer) {
      const videoId = video.id;
      playbackUpdateTimer = setInterval(() => {
        if (currentTime > 0 && Math.abs(currentTime - lastReportedTime) > 3) {
          lastReportedTime = currentTime;
          void postJellyfinSessionProgress("Playing/Progress", {
            ItemId: videoId,
            MediaSourceId: playerProps.mediaSourceId,
            PlaySessionId: playerProps.playSessionId,
            PositionTicks: Math.round(currentTime * 10_000_000),
          }).catch(() => {});
        }
      }, 10_000);
    }
  }

  async function handleVideoEnded() {
    if (!video || !playerProps) return;
    if (playbackUpdateTimer) {
      clearInterval(playbackUpdateTimer);
      playbackUpdateTimer = null;
    }
    try {
      // Report the real end position so the backend derives completion (and tears
      // down the transcode session), then explicitly mark played as a guarantee.
      await postJellyfinSessionProgress("Playing/Stopped", {
        ItemId: video.id,
        MediaSourceId: playerProps.mediaSourceId,
        PlaySessionId: playerProps.playSessionId,
        PositionTicks: Math.round(currentTime * 10_000_000),
      });
      await markJellyfinUserPlayedItem(video.id, true);
    } catch {
      // best-effort
    }
  }

  function handleActiveSubtitleChange(id: string | null) {
    activeSubtitleId = id;
    subtitleChoiceLocked = true;
    if (typeof window !== "undefined" && video) {
      window.localStorage.setItem(`prismedia:subtitle-lang:${video.id}`, id ?? "__off__");
    }
  }

  async function handleAudioTrackChange(streamIndex: number) {
    if (!video) return;
    selectedAudioStreamIndex = streamIndex;
    playbackInfo = await loadPlaybackInfo(video.id, playbackInfo?.PlaySessionId, streamIndex);
  }

  function handleSeek(time: number) {
    playerHandle?.seekTo(time);
  }

  // ── Transcript dock ────────────────────────────────────────────────

  function toggleTranscriptDock() {
    userWantsDock = !userWantsDock;
    if (typeof window !== "undefined") {
      writeTranscriptDockPreference(window.localStorage, userWantsDock);
    }
  }

  function handleResizeStart(event: PointerEvent) {
    event.preventDefault();
    isResizing = true;
    (event.currentTarget as Element | null)?.setPointerCapture?.(event.pointerId);
  }

  function handleResizeMove(event: PointerEvent) {
    if (!isResizing) return;
    const container = videoWrapperEl?.parentElement as HTMLElement | null;
    if (!container) return;
    const rect = container.getBoundingClientRect();
    if (rect.width <= 0) return;
    const pct = ((event.clientX - rect.left) / rect.width) * 100;
    dockVideoPercent = clampTranscriptDockPercent(pct);
  }

  function handleResizeEnd(event: PointerEvent) {
    if (!isResizing) return;
    isResizing = false;
    try {
      (event.currentTarget as Element | null)?.releasePointerCapture?.(event.pointerId);
    } catch {
      // already released
    }
    if (typeof window !== "undefined") {
      writeTranscriptDockWidth(window.localStorage, dockVideoPercent);
    }
  }

  // ── Entity mutations ───────────────────────────────────────────────

  async function handleRatingChange(value: number | null) {
    if (!video || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(video, value, (next) => (video = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!video) return;
    await toggleOptimisticEntityFlag(video, "isFavorite", (next) => (video = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!video) return;
    await toggleOptimisticEntityFlag(video, "isOrganized", (next) => (video = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!video) return;
    await updateEntityMetadata(video.id, request, { kind: video.kind });
    await refreshVideo();
  }
</script>

<svelte:head>
  <title>{video?.title ?? "Video"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <div class="player-skeleton" aria-hidden="true"></div>
    <EntityDetailSkeleton showHero={false} tabCount={4} />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load video."}</p>
      <button type="button" onclick={() => void loadVideo()}>Retry</button>
    </div>
  {:else if card && video && playerProps}
    <NsfwBlur isNsfw={flagsNsfw}>
      <div class={cn(isTranscriptDocked && "lg:flex lg:items-start lg:gap-0")}>
        <div
          bind:this={videoWrapperEl}
          class={cn("player-surface", isTranscriptDocked && "lg:min-w-0")}
          style={isTranscriptDocked ? `flex: 0 0 ${dockVideoPercent}%` : undefined}
        >
          <VideoPlayer
            bind:handle={playerHandle}
            src={playerProps.src}
            directSrc={playerProps.directSrc}
            codec={playerProps.codec}
            sourceWidth={playerProps.sourceWidth}
            sourceHeight={playerProps.sourceHeight}
            colorPipelineLabel={playerProps.colorPipelineLabel}
            poster={playerProps.poster}
            mediaTitle={video.title}
            mediaArtist={seriesRef?.title}
            markers={playerProps.markers}
            duration={playerProps.duration || undefined}
            onPlayStarted={handlePlayStarted}
            onTimeUpdate={handleTimeUpdate}
            trickplayPlaylist={playerProps.trickplayPlaylist}
            subtitleTracks={playerProps.subtitleTracks}
            audioTrackOptions={playerProps.audioTracks}
            onAudioTrackChange={handleAudioTrackChange}
            activeSubtitleTrackId={activeSubtitleId}
            onActiveSubtitleTrackIdChange={handleActiveSubtitleChange}
            {subtitleChoiceLocked}
            {subtitleDefaults}
            isTranscriptSidecarOpen={userWantsDock && hasSubtitles}
            onTranscriptSidecarToggle={toggleTranscriptDock}
            {defaultPlaybackMode}
            onForceTranscode={forceTranscodeFallback}
            {showCastControls}
            onEnded={handleVideoEnded}
          />
          {#if isTranscriptInlineDocked}
            <div class="mt-2 lg:hidden">
              <VideoTranscriptPanel
                videoId={video.id}
                tracks={playerProps.subtitleTracks}
                activeTrackId={activeSubtitleId}
                onActiveTrackIdChange={handleActiveSubtitleChange}
                currentTime={displayTime}
                onSeek={handleSeek}
                onTracksChanged={refreshVideo}
                variant="compact"
                isDocked
                onDockToggle={toggleTranscriptDock}
              />
            </div>
          {/if}
        </div>
        {#if isTranscriptDocked}
          <div
            role="separator"
            aria-label="Resize transcript panel"
            aria-orientation="vertical"
            onpointerdown={handleResizeStart}
            onpointermove={handleResizeMove}
            onpointerup={handleResizeEnd}
            onpointercancel={handleResizeEnd}
            class="hidden lg:flex w-2 shrink-0 cursor-col-resize items-center justify-center bg-surface-3 hover:bg-accent-950 active:bg-accent-950 transition-colors group"
            style={`touch-action: none; ${videoWrapperHeight != null ? `height: ${videoWrapperHeight}px;` : ""}`}
          >
            <span
              class="h-8 w-[2px] bg-border-default group-hover:bg-border-accent group-active:bg-border-accent transition-colors"
            ></span>
          </div>
          <div
            class="hidden lg:flex lg:flex-col lg:flex-1 lg:min-w-0 lg:overflow-hidden"
            style={videoWrapperHeight != null ? `height: ${videoWrapperHeight}px` : undefined}
          >
            <VideoTranscriptPanel
              videoId={video.id}
              tracks={playerProps.subtitleTracks}
              activeTrackId={activeSubtitleId}
              onActiveTrackIdChange={handleActiveSubtitleChange}
              currentTime={displayTime}
              onSeek={handleSeek}
              onTracksChanged={refreshVideo}
              variant="list-only"
              isDocked
              onDockToggle={toggleTranscriptDock}
            />
          </div>
        {/if}
      </div>
    </NsfwBlur>

    <EntityDetail
      {card}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      showHero={false}
      posterSize="none"
      tabs={detailTabs}
      sections={detailSections}
      actionButtons={heroActions}
    >
      {#snippet heroMeta()}
        {#if primaryStudio}
          <a href={resolveEntityHref(primaryStudio.kind, primaryStudio.id)} class="meta-item is-studio">{primaryStudio.title}</a>
        {/if}
        {#each dates as date, i (date.code)}
          {#if primaryStudio || i > 0}
            <span class="meta-sep"></span>
          {/if}
          <span class="meta-item">{date.value}</span>
        {/each}
      {/snippet}


      {#snippet sectionContent(section)}
        <VideoDetailSectionContent
          {section}
          {card}
          {studioCards}
          {creditCards}
          seriesCards={seriesCard ? [seriesCard] : []}
          {videoId}
          {playbackState}
          {durationSeconds}
          {playbackBusy}
          {playerProps}
          {isTranscriptDockActive}
          {isTranscriptDocked}
          {hasSubtitles}
          {activeSubtitleId}
          {displayTime}
          getCurrentTime={() => currentTime}
          onSeek={handleSeek}
          onResume={handleResume}
          onStartOver={handleStartOver}
          onToggleWatched={handleToggleWatched}
          onRefresh={refreshVideo}
          onActiveSubtitleChange={handleActiveSubtitleChange}
          onTranscriptDockToggle={toggleTranscriptDock}
        />
      {/snippet}
    </EntityDetail>
  {/if}
</div>

<style>
  .detail-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
    max-width: none;
    margin: 0;
  }

  .player-skeleton {
    aspect-ratio: 16 / 9;
    background: #050508;
    animation: pulse 1.2s ease-in-out infinite;
  }

  .error-notice {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 1rem;
    border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235));
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.85rem;
  }

  .error-notice button {
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-muted, #8a93a6);
    padding: 0.4rem 0.8rem;
    font-size: 0.78rem;
    cursor: pointer;
  }

  .player-surface {
    background: #050508;
  }

  :global(.meta-item) {
    white-space: nowrap;
    font-size: 0.82rem;
  }

  :global(.meta-item.is-studio) {
    color: var(--color-text-accent, #c49a5a);
    text-decoration: none;
    transition: opacity 0.15s;
  }

  :global(.meta-item.is-studio:hover) {
    opacity: 0.8;
  }

  :global(.meta-sep) {
    display: inline-block;
    width: 3px;
    height: 3px;
    margin: 0 0.5rem;
    background: var(--color-text-muted, #8a93a6);
    opacity: 0.5;
  }

  @keyframes pulse {
    0%, 100% { opacity: 0.45; }
    50% { opacity: 0.85; }
  }
</style>
