<script lang="ts">
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import type {
    EntityDetailCardFull,
  } from "$lib/entities/entity-detail";
  import type { EntityDetailSection } from "$lib/components/entities/EntityDetail.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-relationship-thumbnails";
  import type { ConsumptionState, VideoPlayerProps } from "$lib/entities/video-capabilities";
  import VideoMarkerEditor from "$lib/components/VideoMarkerEditor.svelte";
  import VideoTranscriptPanel from "$lib/components/VideoTranscriptPanel.svelte";
  import VideoPlaybackStatus from "./VideoPlaybackStatus.svelte";
  import { Tv } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";

  interface Props {
    section: EntityDetailSection;
    card: EntityDetailCardFull;
    seriesCards?: EntityThumbnailCard[];
    videoId: string;
    playbackState: ConsumptionState | null;
    durationSeconds: number;
    playbackBusy: boolean;
    playerProps: VideoPlayerProps;
    isTranscriptDockActive: boolean;
    isTranscriptDocked: boolean;
    hasSubtitles: boolean;
    activeSubtitleId: string | null;
    displayTime: number;
    getCurrentTime: () => number;
    onSeek: (time: number) => void;
    onResume: () => void;
    onStartOver: () => void;
    onToggleWatched: (watched: boolean) => void;
    onRefresh: () => void | Promise<void>;
    onActiveSubtitleChange: (id: string | null) => void;
    onTranscriptDockToggle: () => void;
  }

  let {
    section,
    card,
    seriesCards = [],
    videoId,
    playbackState,
    durationSeconds,
    playbackBusy,
    playerProps,
    isTranscriptDockActive,
    isTranscriptDocked,
    hasSubtitles,
    activeSubtitleId,
    displayTime,
    getCurrentTime,
    onSeek,
    onResume,
    onStartOver,
    onToggleWatched,
    onRefresh,
    onActiveSubtitleChange,
    onTranscriptDockToggle,
  }: Props = $props();
</script>

{#if section.id === "related"}
  <EntityCastAndCrewSection
    relatedCards={seriesCards}
    relatedLabel="Series"
    relatedIcon={Tv}
  />
{:else if section.id === "playback"}
  {#if playbackState}
    <VideoPlaybackStatus
      accessCount={playbackState.accessCount}
      completionCount={playbackState.completionCount}
      activeSeconds={playbackState.activeSeconds}
      resumeSeconds={playbackState.resumeSeconds}
      {durationSeconds}
      completedAt={playbackState.completedAt}
      livePositionSeconds={displayTime}
      busy={playbackBusy}
      {onResume}
      {onStartOver}
      {onToggleWatched}
    />
  {/if}
{:else if section.id === "markers"}
  <VideoMarkerEditor
    entityId={videoId}
    markers={card.markers}
    {getCurrentTime}
    {displayTime}
    {onSeek}
    onRefresh={onRefresh}
  />
{:else if section.id === "transcript"}
  {#if isTranscriptDockActive}
    <div class="transcript-tab-stack">
      <div class="tab-inline-notice">
        <span>
          {isTranscriptDocked
            ? "Transcript is docked next to the video."
            : "Transcript is docked under the video."}
        </span>
        <Button
          variant="ghost"
          size="sm"
          class="font-mono uppercase tracking-[0.04em]"
          onclick={onTranscriptDockToggle}
        >
          Move it back here
        </Button>
      </div>
      <VideoTranscriptPanel
        {videoId}
        tracks={playerProps.subtitleTracks}
        activeTrackId={activeSubtitleId}
        onActiveTrackIdChange={onActiveSubtitleChange}
        currentTime={displayTime}
        {onSeek}
        onTracksChanged={onRefresh}
        variant="tracks-only"
        isDocked
        onDockToggle={onTranscriptDockToggle}
      />
    </div>
  {:else}
    <VideoTranscriptPanel
      {videoId}
      tracks={playerProps.subtitleTracks}
      activeTrackId={activeSubtitleId}
      onActiveTrackIdChange={onActiveSubtitleChange}
      currentTime={displayTime}
      {onSeek}
      onTracksChanged={onRefresh}
      onDockToggle={hasSubtitles ? onTranscriptDockToggle : undefined}
      isDocked={false}
    />
  {/if}
{/if}

<style>
  .transcript-tab-stack {
    display: grid;
    gap: 0;
    min-width: 0;
  }

  .tab-inline-notice {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    margin-bottom: 0.75rem;
    padding: 1rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.82rem;
  }

  @media (max-width: 640px) {
    .tab-inline-notice {
      align-items: flex-start;
      flex-direction: column;
    }
  }
</style>
