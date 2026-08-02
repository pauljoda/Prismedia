<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import {
    Captions,
    CloudDownload,
    Info,
    MapPin,
    Play,
    SlidersHorizontal,
    Users,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { beforeNavigate, goto } from "$app/navigation";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { fetchSettingsValues, type LibrarySettings } from "$lib/api/settings";
  import { updateEntityConsumption } from "$lib/api/consumption";
  import { reportVideoPlayback } from "$lib/api/playback";
  import type { VideoPlaybackPlanResponse } from "$lib/api/generated/model";
  import {
    loadPlaybackPlan as loadPlaybackPlanRequest,
    negotiateForceTranscodeSrc,
  } from "$lib/player/playback-negotiation";
  import { durationToSeconds } from "$lib/utils/format";
  import { settingKeys, valuesToLibrarySettings } from "$lib/settings/app-settings";
  import { getCapability, isPlayableVideo, isWanted } from "$lib/api/capabilities";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import type { EntityDetailCredit, EntityDetailTag } from "$lib/entities/entity-detail";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import {
    hydrateStandardRelationshipCards,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-relationship-thumbnails";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { CAPABILITY_KIND, CREDIT_ROLE, type EntityKindCode } from "$lib/entities/entity-codes";
  import { extractVideoPlayerProps, getConsumptionState } from "$lib/entities/video-capabilities";
  import { ConsumptionActivityClock } from "$lib/entities/consumption-activity-clock";
  import NsfwBlur from "$lib/components/nsfw/NsfwBlur.svelte";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import VideoPlayer, {
    type VideoPlayerHandle,
  } from "$lib/components/VideoPlayer.svelte";
  import VideoDetailSectionContent from "../../videos/[id]/VideoDetailSectionContent.svelte";
  import VideoTranscriptPanel from "$lib/components/VideoTranscriptPanel.svelte";
  import {
    buildSubtitleDefaults,
    clampTranscriptDockPercent,
    readTranscriptDockPreferences,
    writeTranscriptDockPreference,
    writeTranscriptDockWidth,
  } from "../../videos/[id]/video-page-state";
  import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => page.params.id ?? "",
    load: async ({ signal }) => {
      const nextMovie = await fetchEntity(page.params.id ?? "", { signal });
      const nextVideo = isPlayableVideo(nextMovie.capabilities) ? nextMovie : null;
      const [nextPlaybackPlan, relationships] = await Promise.all([
        nextVideo
          ? loadPlaybackPlan(nextVideo.id, playbackPlan?.sessionId, selectedAudioStreamIndex)
          : null,
        hydrateMovieRelationships(nextMovie, signal),
      ]);
      signal.throwIfAborted();
      video = nextVideo;
      playbackPlan = nextPlaybackPlan;
      relationshipCredits = relationships.credits;
      relationshipStudio = relationships.studio;
      relationshipTags = relationships.relationshipTags;
      return nextMovie;
    },
    breadcrumbs: (currentMovie) => [
      { label: "Movies", href: "/movies" },
      { label: currentMovie.title },
    ],
  });
  const movie = $derived(detail.entity);
  let video = $state<EntityCardFull | null>(null);
  // The acquisition backing this movie (a wanted placeholder still searching/downloading, or the
  // import that produced it), so its state is managed right here instead of only under /request.
  let playbackPlan = $state.raw<VideoPlaybackPlanResponse | null>(null);
  let librarySettings = $state<LibrarySettings | null>(null);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);

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
  const viewingActivityClock = new ConsumptionActivityClock();
  let hydratedSubtitlePrefsKey = "";

  // ── Transcript dock plumbing ───────────────────────────────────────
  let userWantsDock = $state(false);
  let dockVideoPercent = $state(80);
  let isDesktopViewport = $state(false);
  let videoWrapperEl: HTMLDivElement | null = $state(null);
  let videoWrapperHeight = $state<number | null>(null);
  let isResizing = false;

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!movie) return null;
    return {
      ...entityCardToDetailCard(movie),
      tags: relationshipTags,
      credits: relationshipCredits,
      studio: relationshipStudio,
    };
  });
  const videoCard = $derived.by((): EntityDetailCardFull | null => (
    video ? entityCardToDetailCard(video) : null
  ));
  const identifyAction = useIdentifyDetailAction(() => movie);
  const heroActions = $derived.by((): EntityDetailActionButton[] =>
    identifyAction.action ? [identifyAction.action] : []);

  // Wanted/tracking state (Search for release, releases, live download, monitoring) lives in the
  // Acquisition detail tab; the tab only appears while the movie has an acquisition story.
  const acq = useEntityAcquisition({
    entityId: () => movie?.id,
    capabilities: () => movie?.capabilities,
    onChanged: () => detail.reload({ showLoading: false }),
    onStatusChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto("/movies"),
  });
  const wantedStateLabel = $derived(acquisitionStatusDisplay(acq.acquisition?.summary.status).label);
  const fileManagement = {
    onDeleted: () => goto("/movies"),
    onReverted: () => refreshAfterManagedFileRevert(acq, () => detail.reload({ showLoading: false })),
  };
  const videoId = $derived(video?.id ?? "");

  const playerProps = $derived.by(() => {
    if (!video) return null;
    return extractVideoPlayerProps(video.id, video.capabilities, playbackPlan, selectedAudioStreamIndex);
  });

  const primaryStudio = $derived(relationshipStudio);

  // Built-in sections come from EntityDetail's core catalog; only route-specific
  // sections and label overrides are declared here.
  const detailSections = $derived.by((): EntityDetailSection[] => [
    {
      id: "playback",
      label: "Playback",
      icon: Play,
      hidden: !playbackState,
    },
    {
      id: "credits",
      label: "Cast",
      icon: Users,
    },
    {
      id: "markers",
      label: "Markers",
      count: videoCard?.markers.length ?? 0,
    },
    {
      id: "transcript",
      label: "Transcript",
      count: playerProps?.subtitleTracks.length ?? 0,
    },
    { id: "acquisition" },
  ]);

  const detailTabs = $derived.by((): EntityDetailTab[] => {
    if (!card) return [];
    return [
      {
        id: "details",
        label: "Details",
        icon: Info,
        sections: ["description", "playback", "tags", "studio", "credits"],
      },
      {
        id: "metadata",
        label: "Metadata",
        icon: SlidersHorizontal,
        sections: ["stats", "dates", "classification", "technical", "source", "links"],
        layout: "grid",
      },
      {
        id: "markers",
        label: "Markers",
        icon: MapPin,
        count: videoCard?.markers.length ?? 0,
        sections: ["markers"],
      },
      {
        id: "transcript",
        label: "Transcript",
        icon: Captions,
        count: playerProps?.subtitleTracks.length ?? 0,
        sections: ["transcript"],
      },
      ...(acq.visible
        ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
        : []),
    ];
  });

  // The fileless (wanted placeholder) branch shows metadata-only tabs — no playback, markers, or
  // transcript exist yet — plus the same Acquisition tab, which owns the actionable wanted state.
  const wantedDetailTabs = $derived.by((): EntityDetailTab[] => [
    {
      id: "details",
      label: "Details",
      icon: Info,
      sections: ["description", "tags", "studio", "credits"],
    },
    {
      id: "metadata",
      label: "Metadata",
      icon: SlidersHorizontal,
      sections: ["stats", "dates", "classification", "source", "links"],
      layout: "grid",
    },
    ...(acq.visible
      ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
      : []),
  ]);

  const dates = $derived(card?.dates ?? []);

  const flagsNsfw = $derived.by(() => {
    if (!movie) return false;
    const cap = getCapability(movie.capabilities, CAPABILITY_KIND.flags);
    return cap?.isNsfw === true;
  });

  const playbackState = $derived.by(() => {
    if (!video) return null;
    return getConsumptionState(video.capabilities);
  });

  const durationSeconds = $derived.by(() => {
    if (!video) return 0;
    return durationToSeconds(getCapability(video.capabilities, CAPABILITY_KIND.technical)?.duration ?? null) ?? 0;
  });
  const initialPlaybackTime = $derived(
    playbackState && !playbackState.completedAt && playbackState.resumeSeconds > 5
      ? playbackState.resumeSeconds
      : 0,
  );

  let playbackBusy = $state(false);

  /** Resumes inline playback from the stored position and brings the player into view. */
  function handleResume() {
    if (!playbackState) return;
    resumeApplied = true;
    playerHandle?.seekTo(playbackState.resumeSeconds);
    videoWrapperEl?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  /** Marks the movie watched or unwatched via the shared playback capability, then refreshes. */
  async function handleToggleWatched(watched: boolean) {
    if (!video || playbackBusy) return;
    playbackBusy = true;
    try {
      await updateEntityConsumption(video.id, { completed: watched });
      await refreshMovie();
    } catch {
      // best-effort; the card reflects the last known state on failure
    } finally {
      playbackBusy = false;
    }
  }

  /**
   * Resets playback to the beginning through the shared playback capability.
   */
  async function handleStartOver() {
    if (!video || playbackBusy) return;
    playbackBusy = true;
    try {
      await updateEntityConsumption(video.id, { positionSeconds: 0 });
      resumeApplied = true;
      playerHandle?.seekTo(0);
      await refreshMovie();
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

  beforeNavigate(() => {
    flushPlaybackPosition();
  });

  onMount(() => {
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
      flushPlaybackPosition();
      if (playbackUpdateTimer) clearInterval(playbackUpdateTimer);
    };
  });

  // Reset play tracking when video ID changes.
  $effect(() => {
    playTracked = false;
    resumeApplied = false;
    selectedAudioStreamIndex = null;
    lastReportedTime = 0;
    hydratedSubtitlePrefsKey = "";
    if (playbackUpdateTimer) {
      clearInterval(playbackUpdateTimer);
      playbackUpdateTimer = null;
    }
    video?.id;
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

  const entityWanted = $derived(!!movie && isWanted(movie.capabilities));

  /** Cancelling a wanted movie's request deletes the placeholder entity, so this page no longer exists. */
  function handleAcquisitionCancelled() {
    // Cancel stops the download only — the wanted placeholder stays, so the page still exists.
    void detail.reload({ showLoading: false });
  }

  async function refreshMovie() {
    await detail.reload({ showLoading: false });
  }

  async function hydrateMovieRelationships(nextMovie: EntityCardFull, signal: AbortSignal) {
    return hydrateStandardRelationshipCards(nextMovie, { signal });
  }

  async function loadPlaybackPlan(
    videoId: string,
    sessionId?: string | null,
    audioStreamIndex?: number | null,
  ) {
    return await loadPlaybackPlanRequest(videoId, {
      sessionId,
      audioStreamIndex,
    });
  }

  // Player callback: re-negotiate a guaranteed-playable transcode after a fatal decode error.
  async function forceTranscodeFallback(): Promise<string | null> {
    if (!video) return null;
    return negotiateForceTranscodeSrc(video, selectedAudioStreamIndex, playbackPlan?.sessionId);
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

  function flushPlaybackPosition() {
    if (!playTracked || !video || !playerProps || currentTime <= 0) return;
    const activitySeconds = viewingActivityClock.stop();
    void reportVideoPlayback("stop", {
      entityId: video.id,
      sessionId: playerProps.sessionId,
      positionSeconds: currentTime,
      durationSeconds: playerProps.duration,
      activitySeconds,
    }).catch(() => {});
  }

  async function handlePlayStarted() {
    if (playTracked || !video || !playerProps) return;
    playTracked = true;

    try {
      await reportVideoPlayback("start", {
        entityId: video.id,
        sessionId: playerProps.sessionId,
        positionSeconds: currentTime,
        durationSeconds: playerProps.duration,
      });
    } catch {
      // best-effort
    }

    if (!playbackUpdateTimer) {
      const videoId = video.id;
      playbackUpdateTimer = setInterval(() => {
        if (currentTime > 0 && Math.abs(currentTime - lastReportedTime) > 3) {
          lastReportedTime = currentTime;
          const activitySeconds = viewingActivityClock.take();
          void reportVideoPlayback("progress", {
            entityId: videoId,
            sessionId: playerProps.sessionId,
            positionSeconds: currentTime,
            durationSeconds: playerProps.duration,
            activitySeconds,
          }).catch(() => {});
        }
      }, 10_000);
    }
  }

  function handlePlaybackActive() {
    viewingActivityClock.start();
  }

  function handlePlaybackPaused() {
    if (!playTracked || !video || !playerProps) return;
    const activitySeconds = viewingActivityClock.stop();
    if (!activitySeconds) return;
    void reportVideoPlayback("progress", {
      entityId: video.id,
      sessionId: playerProps.sessionId,
      positionSeconds: currentTime,
      durationSeconds: playerProps.duration,
      activitySeconds,
    }).catch(() => {});
  }

  async function handleVideoEnded() {
    if (!video || !playerProps) return;
    if (playbackUpdateTimer) {
      clearInterval(playbackUpdateTimer);
      playbackUpdateTimer = null;
    }
    const activitySeconds = viewingActivityClock.stop();
    try {
      await reportVideoPlayback("stop", {
        entityId: video.id,
        sessionId: playerProps.sessionId,
        positionSeconds: currentTime,
        durationSeconds: playerProps.duration,
        completed: true,
        activitySeconds,
      });
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
    playbackPlan = await loadPlaybackPlan(video.id, playbackPlan?.sessionId, streamIndex);
  }

  function handleSeek(time: number) {
    playerHandle?.seekTo(time);
  }

  function detailCardForSection(section: EntityDetailSection): EntityDetailCardFull {
    return section.id === "technical" || section.id === "source" || section.id === "markers"
      ? (videoCard ?? card!)
      : card!;
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

</script>

<svelte:head>
  <title>{movie?.title ?? "Movie"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if detail.loadState === "loading"}
    <div class="player-skeleton" aria-hidden="true"></div>
  {/if}
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load movie."
    onRetry={detail.retry}
    tabCount={4}
  >
  {#if card && videoCard && video && playerProps}
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
            resolutionLabel={playerProps.resolutionLabel}
            dynamicRangeLabel={playerProps.dynamicRangeLabel}
            videoCodecLabel={playerProps.videoCodecLabel}
            audioFormatLabel={playerProps.audioFormatLabel}
            streamMethod={playerProps.streamMethod}
            qualityRungs={playerProps.qualityRungs}
            poster={playerProps.poster}
            mediaTitle={video?.title}
            mediaArtist={primaryStudio?.title}
            markers={playerProps.markers}
            duration={playerProps.duration || undefined}
            initialTime={initialPlaybackTime}
            onPlayStarted={handlePlayStarted}
            onPlaybackActive={handlePlaybackActive}
            onPlaybackPaused={handlePlaybackPaused}
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
                onTracksChanged={refreshMovie}
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
              onTracksChanged={refreshMovie}
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
      wantedStatus={acq.acquisition?.summary.status ?? null}
      onRatingChange={detail.changeRating}
      onFavoriteToggle={detail.toggleFavorite}
      onOrganizedToggle={detail.toggleOrganized}
      onMetadataSave={detail.saveMetadata}
      ratingBusy={detail.ratingBusy}
      showHero
      posterSize="large"
      tabs={detailTabs}
      sections={detailSections}
      actionButtons={heroActions}
      defaultCreditRole={CREDIT_ROLE.actor}
    >
      {#snippet heroMeta()}
        {#if primaryStudio}
          <a href={resolveEntityHref(primaryStudio.kind as EntityKindCode, primaryStudio.id)} class="meta-item is-studio">{primaryStudio.title}</a>
        {/if}
        <EntityDetailHeroDates {dates} leadingSeparator={Boolean(primaryStudio)} />
      {/snippet}


      {#snippet sectionContent(section)}
        {#if section.id === "acquisition"}
          <EntityAcquisitionCard
            {acq}
            entity={movie}
            {fileManagement}
            onCancelled={handleAcquisitionCancelled}
            onImported={refreshMovie}
          />
        {:else}
          <VideoDetailSectionContent
            {section}
            card={detailCardForSection(section)}
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
            onRefresh={refreshMovie}
            onActiveSubtitleChange={handleActiveSubtitleChange}
            onTranscriptDockToggle={toggleTranscriptDock}
          />
        {/if}
      {/snippet}
    </EntityDetail>
  {:else if card}
    <!-- Fileless movie (a wanted request placeholder): metadata plus the Acquisition tab —
         wanted/tracking state is managed on the entity itself, same as books. -->
    <EntityDetail
      {card}
      wantedStatus={acq.acquisition?.summary.status ?? null}
      onRatingChange={detail.changeRating}
      onFavoriteToggle={detail.toggleFavorite}
      onOrganizedToggle={detail.toggleOrganized}
      onMetadataSave={detail.saveMetadata}
      ratingBusy={detail.ratingBusy}
      showHero
      posterSize="large"
      actionButtons={heroActions}
      tabs={wantedDetailTabs}
      sections={detailSections}
      defaultCreditRole={CREDIT_ROLE.actor}
    >
      {#snippet heroMeta()}
        {#if primaryStudio}
          <a href={resolveEntityHref(primaryStudio.kind as EntityKindCode, primaryStudio.id)} class="meta-item is-studio">{primaryStudio.title}</a>
        {/if}
        <EntityDetailHeroDates {dates} leadingSeparator={Boolean(primaryStudio)} />
      {/snippet}

      {#snippet heroBadges()}
        {#if entityWanted}
          <span class="hero-badge wanted">{wantedStateLabel}</span>
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "acquisition"}
          <EntityAcquisitionCard
            {acq}
            entity={movie}
            {fileManagement}
            onCancelled={handleAcquisitionCancelled}
            onImported={refreshMovie}
          />
        {/if}
      {/snippet}
    </EntityDetail>
  {/if}
  </EntityDetailPageState>
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

  .player-surface {
    background: #050508;
  }

  :global(.meta-item) {
    white-space: nowrap;
    font-size: 0.82rem;
  }

  :global(.meta-item.is-studio) {
    color: var(--color-text-accent, #c7c9cc);
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
