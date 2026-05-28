<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { Clock3, Loader2, ScanSearch } from "@lucide/svelte";
  import {
    fetchIdentifyProviders,
    fetchOptionalIdentifyQueueItem,
    providerCanIdentifyKind,
  } from "$lib/api/identify-client";
  import type { IdentifyQueueItem } from "$lib/api/identify-types";

  interface Props {
    entityId: string;
    entityKind?: string;
    title?: string;
    label?: string;
    class?: string;
  }

  let { entityId, entityKind, label = "Identify", class: className }: Props = $props();

  let queuedItem: IdentifyQueueItem | null = $state(null);
  let hasReadyProvider = $state(true);
  let loading = $state(true);

  const isQueued = $derived.by(() => queuedItem !== null && isActiveQueueState(queuedItem.state));
  const disabled = $derived(loading || (!isQueued && !hasReadyProvider));
  const buttonLabel = $derived(
    loading
      ? "Checking"
      : isQueued
        ? "Pending Review"
        : hasReadyProvider
          ? label
          : "No Provider",
  );

  onMount(() => {
    let cancelled = false;
    void loadStatus().finally(() => {
      if (!cancelled) loading = false;
    });

    return () => {
      cancelled = true;
    };
  });

  async function loadStatus() {
    if (!entityId) return;
    const [queueItem, providers] = await Promise.all([
      fetchOptionalIdentifyQueueItem(entityId).catch(() => null),
      entityKind ? fetchIdentifyProviders(entityKind).catch(() => []) : Promise.resolve(null),
    ]);
    queuedItem = queueItem;
    hasReadyProvider = entityKind
      ? (providers ?? []).some((provider) => providerCanIdentifyKind(provider, entityKind))
      : true;
  }

  function isActiveQueueState(state: IdentifyQueueItem["state"]): boolean {
    return state !== "done" && state !== "deleted";
  }

  function navigate() {
    if (disabled) return;
    const params = new URLSearchParams({ returnId: entityId });
    if (isQueued) params.set("queued", "1");
    void goto(`/identify/${entityId}?${params.toString()}`);
  }
</script>

<button
  type="button"
  class={[
    "entity-action-button",
    isQueued && "entity-action-button-active",
    className,
  ]}
  disabled={disabled}
  onclick={navigate}
  title={isQueued ? "Open pending Identify review" : hasReadyProvider ? "Queue Identify review" : `No enabled Identify provider supports ${entityKind ?? "this entity"}`}
>
  {#if loading}
    <Loader2 class="h-4 w-4 animate-spin" />
  {:else if isQueued}
    <Clock3 class="h-4 w-4" />
  {:else}
    <ScanSearch class="h-4 w-4" />
  {/if}
  <span class="entity-action-button-label">{buttonLabel}</span>
</button>
