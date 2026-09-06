<script lang="ts">
  import { ListMusic, LoaderCircle, Play, Search, Shuffle } from "@lucide/svelte";
  import { Alert, Badge, Button, Empty, SearchInput } from "@prismedia/ui-svelte";
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
  class="flex flex-col gap-5"
  style:--entity-accent={pageAccent.primary}
  style:--entity-accent-secondary={pageAccent.secondary}
>
  <header class="tracks-page-head">
    <div class="tracks-title-group">
      <h1 class="tracks-title">
        <ListMusic class="h-5 w-5 text-text-muted" />
        Tracks
        {#if !loading && !errorMessage}
          <Badge>{tracks.length}</Badge>
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
    <Alert.Root variant="destructive" class="flex flex-wrap items-center justify-between gap-4 p-4">
      <Alert.Description>{errorMessage}</Alert.Description>
      <Button variant="outline" size="sm" onclick={() => void loadTracks(nsfw.mode === "off")}>
        Retry
      </Button>
    </Alert.Root>
  {:else if loading}
    <div role="status" class="flex min-h-48 items-center justify-center gap-2 text-sm text-muted-foreground">
      <LoaderCircle class="h-4 w-4 animate-spin text-text-accent" />
      Loading tracks…
    </div>
  {:else if tracks.length === 0}
    <Empty.Root>
      <Empty.Header>
        <Empty.Media variant="icon"><ListMusic /></Empty.Media>
        <Empty.Title>No tracks yet</Empty.Title>
        <Empty.Description>Scan a music library to add your tracks.</Empty.Description>
      </Empty.Header>
    </Empty.Root>
  {:else}
    <SearchInput
      class="max-w-xl"
      placeholder="Search tracks, artists, or albums"
      ariaLabel="Search tracks"
      bind:value={query}
    />

    {#if visibleTracks.length > 0}
      <AudioTrackList
        tracks={visibleTracks}
        artworkUrls={albumCoverUrls}
        activeTrackId={playback.currentTrack?.id ?? null}
        isPlaying={playback.playing}
        onPlay={playTrack}
        selectable={false}
        groupBySection={false}
      />
    {:else}
      <Empty.Root>
        <Empty.Header>
          <Empty.Media variant="icon"><Search /></Empty.Media>
          <Empty.Title>No matching tracks</Empty.Title>
          <Empty.Description>No tracks match "{query.trim()}".</Empty.Description>
        </Empty.Header>
        <Empty.Content>
          <Button variant="outline" onclick={() => (query = "")}>Clear search</Button>
        </Empty.Content>
      </Empty.Root>
    {/if}
  {/if}
</section>

<style>
  .tracks-page-head {
    display: flex;
    flex-wrap: wrap;
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

  @media (max-width: 640px) {
    .tracks-page-head {
      align-items: center;
    }
  }
</style>
