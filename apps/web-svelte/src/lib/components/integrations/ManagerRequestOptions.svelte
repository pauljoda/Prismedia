<script lang="ts">
  import { onMount } from "svelte";
  import { Alert, Button, Select, buttonVariants } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, PreparedWantedMovieResponse } from "$lib/api/generated/model";
  import { fetchConnections } from "$lib/api/connections";
  import { resolveEntityHref } from "$lib/entities/entity-codes";
  import ManagedRequests from "./ManagedRequests.svelte";

  let { disabled = false, onPrepare, onActiveChanged }: {
    disabled?: boolean;
    onPrepare: () => Promise<PreparedWantedMovieResponse>;
    onActiveChanged: (active: boolean) => void;
  } = $props();
  let connections = $state<ConnectionResponse[]>([]);
  let connectionId = $state("");
  let prepared = $state<PreparedWantedMovieResponse | null>(null);
  let busy = $state(false);
  let error = $state<string | null>(null);
  const connection = $derived(connections.find(item => item.id === connectionId));
  let alive = true;
  onMount(() => {
    void fetchConnections().then(items => {
      if (!alive) return;
      connections = items.filter(item => item.enabled && item.status === CONNECTION_STATUS.ready && item.effectiveCapabilities.some(capability =>
        capability.kind === PLUGIN_CAPABILITY.externalManager && capability.entityKinds.includes(ENTITY_KIND.movie)
        && capability.operations.includes(INTEGRATION_OPERATION.lookupManaged) && capability.operations.includes(INTEGRATION_OPERATION.ensureManaged)));
    }).catch(cause => { if (alive) error = cause instanceof Error ? cause.message : "Could not load manager connections"; });
    return () => { alive = false; };
  });
  async function prepare() {
    if (!connection || disabled || busy) return;
    busy = true; error = null;
    try { const result = await onPrepare(); if (alive) prepared = result; }
    catch (cause) { if (alive) error = cause instanceof Error ? cause.message : "Could not save the reviewed movie"; }
    finally { if (alive) busy = false; }
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
        <p class="text-sm text-text-muted">Save the reviewed metadata as a wanted movie, then review {connection.name}'s library, quality, monitoring, and search settings. Acquisition starts after that review.</p>
        <Button variant="primary" disabled={disabled || busy} onclick={prepare}>{busy ? "Saving metadata…" : "Save metadata and review manager request"}</Button>
      {:else if prepared.hasFile}
        <p class="text-sm text-text-muted">This movie already has a library source. Use connected-library matching to link existing files.</p>
        <a class={buttonVariants({ variant: "secondary", size: "sm" })} href={resolveEntityHref(ENTITY_KIND.movie, prepared.entityId) ?? "/request"}>Open in library</a>
      {:else}
        <p class="text-sm text-text-muted">Metadata saved. This movie stays wanted if you leave before submitting the manager request.</p>
        <ManagedRequests {connection} initialEntity={{ id: prepared.entityId, title: prepared.title, thumbnailUrl: null }} />
      {/if}
    {/if}
  </div>
{/if}
