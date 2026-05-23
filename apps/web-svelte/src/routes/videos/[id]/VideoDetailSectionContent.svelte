<script lang="ts">
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import type {
    EntityDetailCardFull,
  } from "$lib/entities/entity-detail";
  import type { EntityDetailSection } from "$lib/components/entities/EntityDetail.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-relationship-thumbnails";
  import type { PlaybackState, VideoPlayerProps } from "$lib/entities/video-capabilities";
  import VideoMarkerEditor from "$lib/components/VideoMarkerEditor.svelte";
  import VideoTranscriptPanel from "$lib/components/VideoTranscriptPanel.svelte";
  import { formatVideoTimestamp } from "./video-page-state";

  interface Props {
    section: EntityDetailSection;
    card: EntityDetailCardFull;
    studioCards: EntityThumbnailCard[];
    creditCards: EntityThumbnailCard[];
    videoId: string;
    playbackState: PlaybackState | null;
    playerProps: VideoPlayerProps;
    isTranscriptDockActive: boolean;
    isTranscriptDocked: boolean;
    hasSubtitles: boolean;
    activeSubtitleId: string | null;
    displayTime: number;
    getCurrentTime: () => number;
    onSeek: (time: number) => void;
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
    playerProps,
    isTranscriptDockActive,
    isTranscriptDocked,
    hasSubtitles,
    activeSubtitleId,
    displayTime,
    getCurrentTime,
    onSeek,
    onRefresh,
    onActiveSubtitleChange,
    onTranscriptDockToggle,
  }: Props = $props();
</script>

{#if section.id === "cast-and-crew"}
  <EntityCastAndCrewSection {studioCards} {creditCards} />
{:else if section.id === "technical"}
  {#if card.technical.length > 0}
    <div class="tab-data-list">
      {#each card.technical as row (row.label)}
        <div class="tab-data-row">
          <span>{row.label}</span>
          <strong>{row.value}</strong>
        </div>
      {/each}
    </div>
  {/if}
{:else if section.id === "dates"}
  {#if card.dates.length > 0}
    <div class="tab-data-list">
      {#each card.dates as row (row.code)}
        <div class="tab-data-row">
          <span>{row.label}</span>
          <strong>{row.value}</strong>
        </div>
      {/each}
    </div>
  {/if}
{:else if section.id === "playback"}
  {#if playbackState}
    <div class="tab-data-list">
      <div class="tab-data-row">
        <span>Play Count</span>
        <strong>{playbackState.playCount}</strong>
      </div>
      {#if playbackState.resumeSeconds > 0}
        <div class="tab-data-row">
          <span>Resume</span>
          <strong>{formatVideoTimestamp(playbackState.resumeSeconds)}</strong>
        </div>
      {/if}
    </div>
  {/if}
{:else if section.id === "source"}
  {#if card.sources.length > 0 || card.fingerprints.length > 0}
    <div class="tab-data-list">
      {#each card.sources as source (source.code)}
        <div class="tab-data-row">
          <span>{source.code}</span>
          <strong>{source.value}</strong>
        </div>
      {/each}
      {#each card.fingerprints as fingerprint (`${fingerprint.algorithm}:${fingerprint.value}`)}
        <div class="tab-data-row">
          <span>{fingerprint.algorithm}</span>
          <strong>{fingerprint.value}</strong>
        </div>
      {/each}
    </div>
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
  .tab-data-list,
  .transcript-tab-stack {
    display: grid;
    gap: 0;
    min-width: 0;
  }

  .tab-data-row {
    display: grid;
    grid-template-columns: minmax(5.5rem, max-content) minmax(0, 1fr);
    gap: 0.8rem;
    align-items: baseline;
    min-width: 0;
    padding: 0.55rem 0;
    border-bottom: 1px solid color-mix(in srgb, var(--color-border, #1c2235) 56%, transparent);
    font-size: 0.82rem;
  }

  .tab-data-row span {
    color: var(--color-text-muted, #8a93a6);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    letter-spacing: 0.04em;
    text-transform: uppercase;
  }

  .tab-data-row strong {
    min-width: 0;
    overflow-wrap: anywhere;
    color: var(--color-text-secondary, #c4c9d4);
    font-weight: 500;
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
