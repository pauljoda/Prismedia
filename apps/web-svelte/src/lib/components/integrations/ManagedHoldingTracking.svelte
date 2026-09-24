<script lang="ts">
  import { goto } from "$app/navigation";
  import { onMount, untrack } from "svelte";
  import { ArrowUpRight } from "@lucide/svelte";
  import { Alert, Badge, Button } from "@prismedia/ui-svelte";
  import { BOOK_RENDITION, ENTITY_KIND, MANAGED_TRACKING_STATUS } from "$lib/api/generated/codes";
  import type { BookRenditionCode } from "$lib/api/generated/codes";
  import type { ManagedLibraryItem, ManagedTrackingPreview, ManagedTrackingResponse } from "$lib/api/generated/model";
  import { fetchManagedTracking, previewTracking, saveManagedTracking, refreshTracking } from "$lib/api/managed-libraries";
  import { displayNameForEntityKind } from "$lib/entities/entity-codes";
  import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";
  import { isTrackedManagedItem } from "$lib/integrations/managed-item-identity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import ManagedHoldingControls from "./ManagedHoldingControls.svelte";
  import ManagedHoldingRelease from "./ManagedHoldingRelease.svelte";

  import { createUuid } from "$lib/utils/uuid";
  import { isManagedHoldingEstablished, managedTrackingStatusLabels } from "$lib/integrations/managed-labels";
  let { connectionId, connectionName = "Connected app", item = null, bookRendition = null, showControls = false, canControl = false, canRelease = false, compact = false, onLoaded }: { connectionId: string; connectionName?: string; item?: ManagedLibraryItem | null; bookRendition?: BookRenditionCode | null; showControls?: boolean; canControl?: boolean; canRelease?: boolean; compact?: boolean; onLoaded?: (holdings: ManagedTrackingResponse[]) => void } = $props();
  const nsfw = useNsfw();
  let expandedId = $state<string | null>(null);
  let holdings = $state<ManagedTrackingResponse[]>([]);
  let preview = $state<ManagedTrackingPreview | null>(null);
  let error = $state<string | null>(null);
  let busy = $state(false);
  let openingEntityId = $state<string | null>(null);
  let trackingKnown = $state(false);
  let operationId = "";
  let active = true;
  let loadSequence = 0;
  const trackingScope = $derived(JSON.stringify([
    connectionId,
    item?.entityKind ?? null,
    item?.remoteId ?? null,
    bookRendition,
    Object.entries(item?.externalIds ?? {}).sort(([left], [right]) => left.localeCompare(right)),
  ]));
  const visible = $derived(holdings.filter(holding => (!item || isTrackedManagedItem(holding.item, item))
    && (bookRendition === null || holding.item.bookRendition === bookRendition)));
  const presentedHoldings = $derived.by(() => {
    if (!item) return visible;
    const current = visible.find(holding => holding.status !== MANAGED_TRACKING_STATUS.released);
    return current ? [current] : visible.slice(0, 1);
  });
  const hasCurrentHolding = $derived(visible.some(holding => holding.status !== MANAGED_TRACKING_STATUS.released));
  const primaryHolding = $derived(presentedHoldings[0] ?? null);
  const siblingBookHolding = $derived(item?.entityKind === ENTITY_KIND.book && bookRendition
    ? holdings.find(holding => holding.item.entityKind === ENTITY_KIND.book
      && holding.item.bookRendition !== bookRendition
      && holding.status === MANAGED_TRACKING_STATUS.tracking
      && isTrackedManagedItem(holding.item, item)) ?? null : null);
  const previewBookWorkId = $derived.by(() => {
    const currentPreview = preview;
    if (!currentPreview || item?.entityKind !== ENTITY_KIND.book || !bookRendition) return null;
    const workIds = new Set(currentPreview.sources.filter(source => currentPreview.selections.some(selection =>
      selection.entityId === source.entityId && selection.sourceFileId === source.sourceFileId))
      .map(source => bookRendition === BOOK_RENDITION.audiobook ? source.parentEntityId : source.entityId));
    return workIds.size === 1 ? [...workIds][0] ?? null : null;
  });
  const combinesBookWorks = $derived(!!siblingBookHolding?.bookWorkId && !!previewBookWorkId
    && siblingBookHolding.bookWorkId !== previewBookWorkId);
  const statusLabels = managedTrackingStatusLabels;
  $effect(() => {
    const selectedConnectionId = connectionId;
    const scope = trackingScope;
    untrack(() => {
      loadSequence += 1;
      expandedId = null;
      holdings = [];
      preview = null;
      error = null;
      busy = false;
      openingEntityId = null;
      trackingKnown = false;
      operationId = "";
      void load(selectedConnectionId, scope);
    });
  });
  onMount(() => {
    const timer = setInterval(() => { if (!busy) void load(connectionId, trackingScope); }, 15000);
    return () => { active = false; loadSequence += 1; clearInterval(timer); };
  });
  async function load(selectedConnectionId: string, scope: string) {
    const sequence = ++loadSequence;
    try {
      const results = await fetchManagedTracking(selectedConnectionId);
      if (!active || sequence !== loadSequence || scope !== trackingScope) return;
      holdings = results;
      trackingKnown = true;
      error = null;
      onLoaded?.(results);
    } catch (cause) {
      if (!active || sequence !== loadSequence || scope !== trackingScope) return;
      error = cause instanceof Error ? cause.message : "Could not read tracked holdings";
      onLoaded?.(holdings);
    }
  }
  async function match() {
    const selectedItem = item;
    if (!selectedItem) return;
    const scope = trackingScope;
    loadSequence += 1;
    busy = true; error = null; preview = null;
    try {
      const result = await previewTracking(connectionId, { entityKind: selectedItem.entityKind, remoteId: selectedItem.remoteId,
        expectedExternalIds: selectedItem.externalIds, ...(bookRendition ? { bookRendition } : {}) });
      if (active && scope === trackingScope) { preview = result; operationId = createUuid(); }
    } catch (cause) { if (active && scope === trackingScope) error = cause instanceof Error ? cause.message : "Could not match existing items"; }
    finally { if (active && scope === trackingScope) busy = false; }
  }
  async function link() {
    const selectedItem = item;
    const selectedPreview = preview;
    if (!selectedItem || !selectedPreview?.libraryRootId || selectedPreview.reviewReason || !operationId) return;
    const scope = trackingScope;
    loadSequence += 1;
    busy = true; error = null;
    try {
      const saved = await saveManagedTracking(connectionId, { operationId, libraryRootId: selectedPreview.libraryRootId,
        item: { entityKind: selectedItem.entityKind, remoteId: selectedItem.remoteId, expectedExternalIds: selectedItem.externalIds,
          ...(bookRendition ? { bookRendition } : {}) }, selections: selectedPreview.selections,
        ...(combinesBookWorks ? { combineBookWorks: true } : {}) });
      if (active && scope === trackingScope) { holdings = [...holdings.filter(holding => holding.id !== saved.id), saved]; preview = null; }
    } catch (cause) { if (active && scope === trackingScope) error = cause instanceof Error ? cause.message : "Could not link this holding"; }
    finally { if (active && scope === trackingScope) busy = false; }
  }
  async function refresh(id: string) {
    const scope = trackingScope;
    loadSequence += 1;
    busy = true; error = null;
    try { await refreshTracking(connectionId, id); if (scope === trackingScope) await load(connectionId, scope); }
    catch (cause) { if (active && scope === trackingScope) error = cause instanceof Error ? cause.message : "Could not queue refresh"; }
    finally { if (active && scope === trackingScope) busy = false; }
  }
  async function openEntity(entityId: string) {
    const scope = trackingScope;
    openingEntityId = entityId;
    error = null;
    try {
      const href = await resolveEntityHrefById(entityId, { hideNsfw: nsfw.mode !== "show" });
      if (!active || scope !== trackingScope) return;
      if (!href) throw new Error("This item does not have a Prismedia page yet.");
      await goto(href);
    } catch (cause) {
      if (active && scope === trackingScope) {
        error = cause instanceof Error ? cause.message : "Could not open this item in Prismedia";
      }
    } finally {
      if (active && scope === trackingScope && openingEntityId === entityId) openingEntityId = null;
    }
  }
  function localTargets(holding: ManagedTrackingResponse) {
    return [...new Map(holding.targets.map(binding => [
      `${binding.target.kind}:${binding.entityId}`,
      binding,
    ])).values()];
  }
  function targetLabel(target: ManagedTrackingResponse["targets"][number], fallbackTitle: string): string {
    if (target.target.kind === ENTITY_KIND.book) return "Ebook";
    if (target.target.kind === ENTITY_KIND.audioTrack) return "Audiobook";
    if (target.target.kind === ENTITY_KIND.comicInstallment && target.target.issueLabel) return `Issue #${target.target.issueLabel}`;
    if (target.target.kind === ENTITY_KIND.videoEpisode) {
      if (target.target.seasonNumber != null && target.target.episodeNumber != null) {
        return `S${target.target.seasonNumber} E${target.target.episodeNumber}`;
      }
      if (target.target.absoluteNumber != null) return `Episode ${target.target.absoluteNumber}`;
    }
    if (target.target.kind === ENTITY_KIND.movie || target.target.kind === ENTITY_KIND.videoSeries) return fallbackTitle;
    return displayNameForEntityKind(target.target.kind);
  }
  function targetPageId(holding: ManagedTrackingResponse, entityId: string): string {
    return holding.item.entityKind === ENTITY_KIND.book ? holding.bookWorkId ?? entityId : entityId;
  }
  function statusVariant(status: ManagedTrackingResponse["status"]): "default" | "success" | "warning" {
    if (status === MANAGED_TRACKING_STATUS.needsReview || status === MANAGED_TRACKING_STATUS.stale) return "warning";
    return "default";
  }
  function holdingSummary(holding: ManagedTrackingResponse): string {
    const targetCount = localTargets(holding).length;
    const availableCount = holding.bindings.filter(binding => binding.isAvailable).length;
    if (holding.status === MANAGED_TRACKING_STATUS.released) {
      return targetCount > 0
        ? `${targetCount} previously linked ${targetCount === 1 ? "item remains" : "items remain"} in Prismedia. Files and history are retained.`
        : "Tracking has stopped. Files, Prismedia items, and history are retained.";
    }
    if (holding.status === MANAGED_TRACKING_STATUS.removed) {
      return availableCount > 0
        ? `${availableCount} linked ${availableCount === 1 ? "file remains" : "files remain"} available in Prismedia. Metadata and history are retained.`
        : "Metadata and history are retained; no local files remain.";
    }
    if (holding.status === MANAGED_TRACKING_STATUS.releasePending) return "File tracking is paused while Prismedia verifies the stop request.";
    if (holding.status === MANAGED_TRACKING_STATUS.pending) return "Prismedia is verifying the reviewed matches.";
    if (holding.status === MANAGED_TRACKING_STATUS.waitingForFiles) {
      return targetCount > 0
        ? `${targetCount} Prismedia ${targetCount === 1 ? "item is" : "items are"} linked and waiting for files from ${connectionName}.`
        : `Waiting for ${connectionName} to report files that can be matched.`;
    }
    if (holding.bindings.length === 0) {
      return holding.status === MANAGED_TRACKING_STATUS.stale
        ? "The connection is unavailable. Previously linked items are retained, but file availability is unknown."
        : `${targetCount} ${targetCount === 1 ? "item is" : "items are"} linked. No file availability has been observed yet.`;
    }
    const fileSummary = `${availableCount} of ${holding.bindings.length} linked ${holding.bindings.length === 1 ? "file" : "files"} available`;
    return holding.status === MANAGED_TRACKING_STATUS.stale
      ? `Last known check: ${fileSummary}.`
      : `${fileSummary}.`;
  }
</script>

{#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
{#if item || visible.length}
  <section class={compact ? "min-w-0 space-y-3" : "w-full max-w-3xl space-y-5"} aria-label={item ? "Items in Prismedia" : "Followed library items"}>
    {#if item}
      <p class="text-sm leading-relaxed text-text-muted">
        {#if primaryHolding?.status === MANAGED_TRACKING_STATUS.removed}
          This title was removed from {connectionName}. Prismedia retains its metadata, links, and history.
        {:else if primaryHolding?.status === MANAGED_TRACKING_STATUS.tracking || primaryHolding?.status === MANAGED_TRACKING_STATUS.waitingForFiles}
          Prismedia reads {connectionName}'s files in place and follows file changes automatically.
        {:else if hasCurrentHolding}
          Prismedia links this title to existing library items without copying or moving {connectionName}'s files.
        {:else}
          When linked, Prismedia reads {connectionName}'s files in place and follows file changes automatically.
        {/if}
      </p>
    {/if}
    {#each presentedHoldings as holding (holding.id)}
      {@const targets = localTargets(holding)}
      <div class="space-y-3 border-b border-border-subtle pb-4 last:border-b-0 last:pb-0">
        {#if !item}<p class="break-words text-sm font-medium text-text-primary">{holding.title}</p>{/if}
        <div class="flex flex-wrap items-center gap-2">
          <Badge variant={statusVariant(holding.status)}>{statusLabels[holding.status]}</Badge>
          <span class="text-xs text-text-muted">{holdingSummary(holding)}</span>
        </div>
        {#if compact}<Button variant="ghost" size="sm" aria-expanded={expandedId === holding.id} onclick={() => expandedId = expandedId === holding.id ? null : holding.id}>{expandedId === holding.id ? "Hide details" : "View details"}</Button>{/if}
        {#if !compact || expandedId === holding.id}
          {#if holding.problem}<p class="break-words text-sm text-text-muted">{holding.problem}</p>{/if}
          {#if item && targets.length > 0}<h3 class="break-words text-sm font-medium text-text-primary">{holding.title}</h3>{/if}
          {#if targets.length === 1}
            {@const target = targets[0]!}
            {@const usesTitle = target.target.kind === ENTITY_KIND.movie || target.target.kind === ENTITY_KIND.videoSeries}
            <div class="flex min-w-0 flex-wrap items-center gap-3">
              {#if !usesTitle}<span class="break-words text-sm text-text-primary">{targetLabel(target, holding.title)}</span>{/if}
              <Button disabled={openingEntityId !== null} size="sm" onclick={() => void openEntity(targetPageId(holding, target.entityId))}>
                {usesTitle ? "Open in Prismedia" : `Open ${targetLabel(target, holding.title)}`}{#if !usesTitle}<span class="sr-only"> in Prismedia</span>{/if}<ArrowUpRight aria-hidden="true" />
              </Button>
              {#if showControls && holding.item.entityKind === ENTITY_KIND.comicSeries && holding.status !== MANAGED_TRACKING_STATUS.removed}
                <ManagedHoldingControls {connectionId} {connectionName} holdingId={holding.id} targetEntityId={target.entityId}
                  targetLabel={targetLabel(target, holding.title)} canPreview={canControl && isManagedHoldingEstablished(holding.status)} />
              {/if}
            </div>
          {:else if targets.length > 1}
            <div class="max-h-72 space-y-1 overflow-y-auto pr-1" aria-label="Linked Prismedia items">
              {#each targets as target (`${target.target.kind}:${target.entityId}`)}
                <div class="flex min-w-0 items-center justify-between gap-3 border-b border-border-subtle py-2 last:border-b-0">
                  <span class="min-w-0 break-words text-sm text-text-primary">{targetLabel(target, holding.title)}</span>
                  <Button variant="ghost" size="sm" disabled={openingEntityId !== null} onclick={() => void openEntity(targetPageId(holding, target.entityId))}>
                    Open<span class="sr-only"> {targetLabel(target, holding.title)} in Prismedia</span><ArrowUpRight aria-hidden="true" />
                  </Button>
                  {#if showControls && holding.item.entityKind === ENTITY_KIND.comicSeries && holding.status !== MANAGED_TRACKING_STATUS.removed}
                    <ManagedHoldingControls {connectionId} {connectionName} holdingId={holding.id} targetEntityId={target.entityId}
                      targetLabel={targetLabel(target, holding.title)} canPreview={canControl && isManagedHoldingEstablished(holding.status)} />
                  {/if}
                </div>
              {/each}
            </div>
          {/if}
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
            {#if holding.status !== MANAGED_TRACKING_STATUS.released && holding.status !== MANAGED_TRACKING_STATUS.removed}
              <Button variant="ghost" size="sm" disabled={busy} onclick={() => void refresh(holding.id)}>Check now</Button>
            {/if}
            {#if holding.lastCheckedAt}<span class="text-xs text-text-muted">Last checked {new Date(holding.lastCheckedAt).toLocaleString()}</span>{/if}
          </div>
          {#if showControls}
            <div class="flex min-w-0 flex-wrap items-start gap-2">
              {#if holding.status !== MANAGED_TRACKING_STATUS.removed && holding.item.entityKind !== ENTITY_KIND.comicSeries}
                <ManagedHoldingControls {connectionId} {connectionName} holdingId={holding.id} canPreview={canControl && isManagedHoldingEstablished(holding.status)} />
              {/if}
              {#if canRelease && (holding.status === MANAGED_TRACKING_STATUS.tracking || holding.status === MANAGED_TRACKING_STATUS.waitingForFiles || holding.status === MANAGED_TRACKING_STATUS.removed)}
                <ManagedHoldingRelease {connectionId} {connectionName} holdingId={holding.id} onaccepted={saved => { loadSequence += 1; holdings = holdings.map(item => item.id === saved.id ? saved : item); }} />
              {/if}
            </div>
          {/if}
        {/if}
      </div>
    {/each}
    {#if item && !trackingKnown && !error}
      <p class="text-sm text-text-muted">Checking Prismedia links…</p>
    {:else if item && trackingKnown && !hasCurrentHolding}
      <div class="space-y-3">
        <div>
          <h3 class="text-sm font-semibold text-text-primary">Find this title in Prismedia</h3>
          <p class="mt-1 text-sm leading-relaxed text-text-muted">Scan the mapped library first, then review the exact matches before linking them to {connectionName}.</p>
        </div>
        <Button variant="secondary" size="sm" disabled={busy} onclick={match}>Find matching items</Button>
      </div>
      {#if preview}
        {#if preview.reviewReason}<p role="status" class="text-sm text-text-muted">{preview.reviewReason}</p>
        {:else}
          <div class="space-y-2">
            <p class="text-sm">{preview.selections.length} existing {preview.selections.length === 1 ? "item matches" : "items match"} the reported files and numbering.</p>
            <div class="max-h-48 space-y-1 overflow-y-auto">
              {#each [...new Set(preview.sources.map(source => source.localPath))] as path (path)}<p class="break-all font-mono text-xs text-text-muted">{path}</p>{/each}
            </div>
            <p class="text-xs text-text-muted">Review these matches before Prismedia starts following file changes from {connectionName}. This mapped library will use reviewed holdings for future scans; other unscanned titles and new episode coverage still require review.</p>
            {#if combinesBookWorks}
              <Alert.Root><Alert.Description>The ebook and audiobook were scanned as separate Book records. Prismedia will combine their files under the Book already linked to {connectionName}, then link this format. Books with reading history, chapters, or other ownership need separate review.</Alert.Description></Alert.Root>
            {/if}
            <Button variant="secondary" disabled={busy} onclick={link}>{combinesBookWorks ? "Combine formats and link" : "Link matching items"}</Button>
          </div>
        {/if}
      {/if}
    {/if}
  </section>
{/if}
