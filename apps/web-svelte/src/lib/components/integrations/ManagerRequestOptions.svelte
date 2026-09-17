<script lang="ts">
  import { onMount } from "svelte";
  import { Alert, Button, Disclosure, Select, buttonVariants } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, EntityKind, PreparedWantedMovieResponse, PreparedWantedSeriesResponse } from "$lib/api/generated/model";
  import { fetchConnections } from "$lib/api/connections";
  import { resolveEntityHref } from "$lib/entities/entity-codes";
  import ManagedRequests from "./ManagedRequests.svelte";

  let { entityKind, disabled = false, onPrepare, onActiveChanged }: {
    entityKind: EntityKind;
    disabled?: boolean;
    onPrepare: () => Promise<PreparedWantedMovieResponse | PreparedWantedSeriesResponse>;
    onActiveChanged: (active: boolean) => void;
  } = $props();
  let connections = $state<ConnectionResponse[]>([]);
  let connectionId = $state("");
  let prepared = $state<PreparedWantedMovieResponse | PreparedWantedSeriesResponse | null>(null);
  let busy = $state(false);
  let error = $state<string | null>(null);
  const connection = $derived(connections.find(item => item.id === connectionId));
  const isSeries = $derived(entityKind === ENTITY_KIND.videoSeries);
  const preparedEntityId = $derived(prepared
    ? "seriesEntityId" in prepared ? prepared.seriesEntityId : prepared.entityId
    : null);
  const preparedEpisodes = $derived(prepared && "episodes" in prepared ? prepared.episodes : []);
  const requestableEpisodes = $derived(preparedEpisodes.filter(episode => !episode.hasFile));
  const ownedEpisodeCount = $derived(preparedEpisodes.length - requestableEpisodes.length);
  let alive = true;
  onMount(() => {
    void fetchConnections().then(items => {
      if (!alive) return;
      connections = items.filter(item => item.enabled && item.status === CONNECTION_STATUS.ready && item.effectiveCapabilities.some(capability =>
        capability.kind === PLUGIN_CAPABILITY.externalManager && capability.entityKinds.includes(entityKind)
        && capability.operations.includes(INTEGRATION_OPERATION.lookupManaged) && capability.operations.includes(INTEGRATION_OPERATION.ensureManaged)));
    }).catch(cause => { if (alive) error = cause instanceof Error ? cause.message : "Could not load manager connections"; });
    return () => { alive = false; };
  });
  async function prepare() {
    if (!connection || disabled || busy) return;
    busy = true; error = null;
    try { const result = await onPrepare(); if (alive) prepared = result; }
    catch (cause) { if (alive) error = cause instanceof Error ? cause.message : `Could not save the reviewed ${isSeries ? "series selection" : "movie"}`; }
    finally { if (alive) busy = false; }
  }
  function episodeLabel(season: number | string, episode: number | string) {
    return `S${String(season).padStart(2, "0")}E${String(episode).padStart(2, "0")}`;
  }
</script>

{#if connections.length || error}
  <div class="space-y-3">
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#if connections.length}
      <Select ariaLabel="Acquisition owner" value={connectionId} disabled={disabled || busy || !!prepared}
        options={[{ value: "", label: "Prismedia downloads" }, ...connections.map(item => ({ value: item.id, label: item.name }))]}
        onchange={value => { connectionId = value; error = null; onActiveChanged(!!value); }} />
    {/if}
    {#if connection}
      {#if !prepared}
        <p class="text-sm text-text-muted">{isSeries
          ? `Save the exact selected episodes, then review ${connection.name}'s library and quality settings. Only those episodes are searched after review.`
          : `Save the reviewed metadata as a wanted movie, then review ${connection.name}'s library, quality, monitoring, and search settings. Acquisition starts after that review.`}</p>
        <Button variant="primary" disabled={disabled || busy} onclick={prepare}>{busy ? "Saving metadata…" : "Save metadata and review manager request"}</Button>
      {:else if !isSeries && "hasFile" in prepared && prepared.hasFile}
        <p class="text-sm text-text-muted">This movie already has a library source. Use connected-library matching to link existing files.</p>
        <a class={buttonVariants({ variant: "secondary", size: "sm" })} href={resolveEntityHref(ENTITY_KIND.movie, prepared.entityId) ?? "/request"}>Open in library</a>
      {:else if isSeries && requestableEpisodes.length === 0}
        <p class="text-sm text-text-muted">All {preparedEpisodes.length} selected episode{preparedEpisodes.length === 1 ? " is" : "s are"} already in the library. No manager request is needed.</p>
        <a class={buttonVariants({ variant: "secondary", size: "sm" })} href={resolveEntityHref(entityKind, preparedEntityId!) ?? "/request"}>Open in library</a>
      {:else}
        <p class="text-sm text-text-muted">{isSeries
          ? `${requestableEpisodes.length} selected episode${requestableEpisodes.length === 1 ? "" : "s"} will be sent to the manager${ownedEpisodeCount ? `; ${ownedEpisodeCount} already-owned episode${ownedEpisodeCount === 1 ? " is" : "s are"} omitted` : ""}.`
          : "Metadata saved. This movie stays wanted if you leave before submitting the manager request."}</p>
        {#if isSeries}
          <Disclosure title="Episodes sent to manager" count={requestableEpisodes.length} open={requestableEpisodes.length <= 5}>
            <ul class="space-y-1.5">
              {#each requestableEpisodes as episode (episode.entityId)}
                <li class="flex min-w-0 gap-2 text-sm">
                  <span class="shrink-0 font-mono text-xs text-text-muted">{episodeLabel(episode.seasonNumber, episode.episodeNumber)}</span>
                  <span class="min-w-0 truncate">{episode.title}</span>
                </li>
              {/each}
            </ul>
          </Disclosure>
        {/if}
        <ManagedRequests
          {connection}
          {entityKind}
          initialEntity={{ id: preparedEntityId!, title: prepared.title, thumbnailUrl: null }}
          initialTargetEntityIds={requestableEpisodes.map(episode => episode.entityId)}
        />
      {/if}
    {/if}
  </div>
{/if}
