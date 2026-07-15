<script lang="ts">
  import { ListMusic, LoaderCircle, Play, Search, Shuffle } from "@lucide/svelte";
  import { Button, TextInput } from "@prismedia/ui-svelte";
  import AudioTrackList from "$lib/components/AudioTrackList.svelte";
  import EntityActionButton from "$lib/components/entities/EntityActionButton.svelte";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { loadAudioTrackLibrary } from "$lib/entities/audio-track-library";
  import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import {
    type PlaybackContext,
    useAudioPlayback,
  } from "$lib/stores/audio-playback.svelte";

  const nsfw = useNsfw();
  const playback = useAudioPlayback()!;
  const pageAccent = entityAccentForKind(ENTITY_KIND.audioTrack);

  let tracks = $state.raw<AudioTrackListItemDto[]>([]);
  let albumCoverUrls = $state.raw<Record<string, string | null>>({});
  let query = $state("");
  let loading = $state(true);
  let errorMessage = $state<string | null>(null);
  let loadGeneration = 0;

  const visibleTracks = $derived.by(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase();
    if (!normalizedQuery) return tracks;
    return tracks.filter((track) =>
      [track.title, track.embeddedArtist, track.embeddedAlbum]
        .some((value) => value?.toLocaleLowerCase().includes(normalizedQuery)),
    );
  });

  function playbackContext(): PlaybackContext {
    return {
      albumTitle: null,
      artistName: null,
      coverUrl: null,
      albumCoverUrls,
    };
  }

  async function loadTracks(hideNsfw: boolean, signal?: AbortSignal) {
    const generation = ++loadGeneration;
    loading = true;
    errorMessage = null;

    try {
      const result = await loadAudioTrackLibrary({ hideNsfw, signal });
      if (generation !== loadGeneration) return;
      tracks = result.tracks;
      albumCoverUrls = result.albumCoverUrls;
    } catch (error) {
      if (signal?.aborted || generation !== loadGeneration) return;
      errorMessage = error instanceof Error ? error.message : "Failed to load tracks.";
    } finally {
      if (generation === loadGeneration) loading = false;
    }
  }

  function playAll() {
    const firstTrack = tracks[0];
    if (!firstTrack) return;
    playback.play(tracks, firstTrack.id, playbackContext(), { shuffle: false });
  }

  function shuffleAll() {
    if (tracks.length === 0) return;
    playback.play(tracks, undefined, playbackContext(), { shuffle: true });
  }

  function playTrack(trackId: string) {
    if (playback.isCurrent(trackId)) {
      playback.toggle();
      return;
    }
    playback.play(tracks, trackId, playbackContext(), { shuffle: false });
  }

  $effect(() => {
    const hideNsfw = nsfw.mode === "off";
    const controller = new AbortController();
    void loadTracks(hideNsfw, controller.signal);
    return () => controller.abort();
  });
</script>

<svelte:head>
  <title>Tracks · Prismedia</title>
</svelte:head>

<section
  class="space-y-5"
  style:--entity-accent={pageAccent.primary}
  style:--entity-accent-secondary={pageAccent.secondary}
>
  <header class="tracks-page-head">
    <div class="tracks-title-group">
      <h1 class="tracks-title">
        <ListMusic class="h-5 w-5 text-text-muted" />
        Tracks
        {#if !loading && !errorMessage}
          <span class="tracks-count">
            {tracks.length}
          </span>
        {/if}
      </h1>
    </div>

    {#if tracks.length > 0}
      <div class="flex items-center gap-2">
        <EntityActionButton
          label="Play All"
          icon={Play}
          iconFill="currentColor"
          variant="primary"
          onClick={playAll}
        />
        <EntityActionButton
          label="Shuffle All"
          icon={Shuffle}
          onClick={shuffleAll}
        />
      </div>
    {/if}
  </header>

  {#if errorMessage}
    <div class="surface-card-sharp flex items-center justify-between gap-4 border-error-500/50 p-4">
      <p class="text-sm text-text-muted">{errorMessage}</p>
      <Button variant="secondary" size="sm" onclick={() => void loadTracks(nsfw.mode === "off")}>
        Retry
      </Button>
    </div>
  {:else if loading}
    <div class="surface-well flex min-h-48 items-center justify-center gap-2 text-sm text-text-muted">
      <LoaderCircle class="h-4 w-4 animate-spin text-text-accent" />
      Loading tracks…
    </div>
  {:else if tracks.length === 0}
    <div class="surface-well p-10 text-center">
      <ListMusic class="mx-auto mb-3 h-6 w-6 text-text-disabled" />
      <h2 class="m-0 font-heading text-base font-semibold text-text-primary">No tracks</h2>
      <p class="mt-1 text-sm text-text-muted">
        No standalone music tracks are in your collection yet. Scan an Artist/Album/Songs library to get started.
      </p>
    </div>
  {:else}
    <div class="track-search max-w-xl">
      <Search class="pointer-events-none absolute left-3 top-1/2 z-10 h-4 w-4 -translate-y-1/2 text-text-disabled" />
      <TextInput
        class="track-search-input"
        type="search"
        placeholder="Search tracks, artists, or albums"
        aria-label="Search tracks"
        value={query}
        oninput={(event) => (query = event.currentTarget.value)}
      />
    </div>

    {#if visibleTracks.length > 0}
      <AudioTrackList
        tracks={visibleTracks}
        artworkUrls={albumCoverUrls}
        activeTrackId={playback.currentTrack?.id ?? null}
        isPlaying={playback.playing}
        onPlay={playTrack}
        selectable={false}
      />
    {:else}
      <div class="surface-well p-8 text-center text-sm text-text-muted">
        No tracks match “{query.trim()}”.
      </div>
    {/if}
  {/if}
</section>

<style>
  .tracks-page-head {
    display: flex;
    align-items: flex-end;
    justify-content: space-between;
    gap: 1rem;
    padding-bottom: 0.5rem;
    border-bottom: 1px solid var(--color-border-subtle);
  }

  .tracks-title-group {
    display: flex;
    align-items: center;
    min-width: 0;
  }

  .tracks-title {
    display: inline-flex;
    align-items: center;
    gap: 0.6rem;
    margin: 0;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.55rem;
    font-weight: 600;
    letter-spacing: -0.025em;
    line-height: 1.05;
  }

  .tracks-count {
    display: inline-flex;
    align-items: center;
    min-height: 1.25rem;
    padding: 0.1rem 0.35rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.65rem;
    font-variant-numeric: tabular-nums;
    letter-spacing: 0;
  }

  .track-search {
    position: relative;
    width: 100%;
  }

  .track-search :global(.track-search-input) {
    padding-left: 2.5rem;
  }

  @media (max-width: 640px) {
    .tracks-page-head {
      align-items: center;
    }
  }
</style>
