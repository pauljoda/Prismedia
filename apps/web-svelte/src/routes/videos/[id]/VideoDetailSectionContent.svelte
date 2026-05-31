<script lang="ts">
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  import type {
    EntityDetailCardFull,
  } from "$lib/entities/entity-detail";
  import type { EntityDetailSection } from "$lib/components/entities/EntityDetail.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-relationship-thumbnails";
  import type { PlaybackState, VideoPlayerProps } from "$lib/entities/video-capabilities";
  import VideoMarkerEditor from "$lib/components/VideoMarkerEditor.svelte";
  import VideoTranscriptPanel from "$lib/components/VideoTranscriptPanel.svelte";
  import VideoPlaybackStatus from "./VideoPlaybackStatus.svelte";
  import { MonitorCog, Calendar, Database } from "@lucide/svelte";

  interface Props {
    section: EntityDetailSection;
    card: EntityDetailCardFull;
    studioCards: EntityThumbnailCard[];
    creditCards: EntityThumbnailCard[];
    videoId: string;
    playbackState: PlaybackState | null;
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
    studioCards,
    creditCards,
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

{#if section.id === "cast-and-crew"}
  <EntityCastAndCrewSection {studioCards} {creditCards} />
{:else if section.id === "technical"}
  {#if card.technical.length > 0}
    <MetadataCard
      title="Technical"
      icon={MonitorCog}
      rows={card.technical.map((r) => ({ label: r.label, value: r.value }))}
    />
  {/if}
{:else if section.id === "dates"}
  {#if card.dates.length > 0}
    <MetadataCard
      title="Dates"
      icon={Calendar}
      rows={card.dates.map((r) => ({ label: r.label, value: r.value }))}
    />
  {/if}
{:else if section.id === "playback"}
  {#if playbackState}
    <VideoPlaybackStatus
      playCount={playbackState.playCount}
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
{:else if section.id === "source"}
  {#if card.sources.length > 0 || card.fingerprints.length > 0}
    <MetadataCard title="Source" icon={Database}
      rows={[
        ...card.sources.map((s) => ({ label: s.code, value: s.value })),
        ...card.fingerprints.map((f) => ({ label: String(f.algorithm), value: f.value })),
      ]}
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
        <button type="button" onclick={onTranscriptDockToggle}>Move it back here</button>
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

  .tab-inline-notice button {
    border: 0;
    background: transparent;
    color: var(--color-text-accent, #c49a5a);
    cursor: pointer;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
  }

  @media (max-width: 640px) {
    .tab-inline-notice {
      align-items: flex-start;
      flex-direction: column;
    }
  }
</style>
