<script lang="ts">
  import { ArrowUpRight, BookOpen, FolderOpen, Headphones } from "@lucide/svelte";
  import { Badge, Panel, buttonVariants } from "@prismedia/ui-svelte";
  import { BOOK_RENDITION, MANAGED_TRACKING_STATUS } from "$lib/api/generated/codes";
  import type { ExternalBookRenditionProvenance } from "$lib/api/generated/model";
  import { getGetPluginIconUrl } from "$lib/api/generated/prismedia";
  import { managedHoldingInputHref, managedHoldingSourceHref } from "$lib/integrations/managed-holding-route";
  import { useSession } from "$lib/stores/session.svelte";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import ManagedHoldingControls from "./ManagedHoldingControls.svelte";
  import ManagedHoldingRelease from "./ManagedHoldingRelease.svelte";

  import { managedRequestPhaseLabels, managedTrackingStatusLabels } from "$lib/integrations/managed-labels";
  let { renditions }: { renditions: readonly ExternalBookRenditionProvenance[] } = $props();
  const session = useSession();
  let releasedHoldingIds = $state<string[]>([]);

  function title(row: ExternalBookRenditionProvenance): string {
    return row.rendition === BOOK_RENDITION.audiobook ? "Audiobook" : "Ebook";
  }

  function sourceHref(row: ExternalBookRenditionProvenance): string {
    const identities = Object.entries(row.holding.item.expectedExternalIds ?? {});
    return identities.length > 0 && identities.every(([provider, value]) => provider.trim().length > 0 && value.trim().length > 0)
      ? managedHoldingInputHref(row.connectionId, row.holding.item)
      : managedHoldingSourceHref(row.connectionId, row.holding.item.entityKind);
  }

  function stateLabel(row: ExternalBookRenditionProvenance): string {
    if (row.holding.status === MANAGED_TRACKING_STATUS.removed || row.holding.status === MANAGED_TRACKING_STATUS.released) {
      return managedTrackingStatusLabels[row.holding.status];
    }
    return managedRequestPhaseLabels[row.request.phase];
  }
</script>

<div class="grid w-full max-w-5xl gap-4 lg:grid-cols-2">
  {#each renditions as row (row.holding.holdingId)}
    {@const removed = row.holding.status === MANAGED_TRACKING_STATUS.removed}
    <Panel class="space-y-4 p-5" aria-label={`${title(row)} external library`}>
      <header class="flex flex-wrap items-start gap-3">
        <PluginIcon name={row.connectionName} iconUrl={getGetPluginIconUrl(row.pluginId)} class="size-9" />
        <div class="min-w-0 flex-1">
          <h3 class="flex items-center gap-2 text-base font-semibold">
            {#if row.rendition === BOOK_RENDITION.audiobook}<Headphones class="size-4" aria-hidden="true" />
            {:else}<BookOpen class="size-4" aria-hidden="true" />{/if}
            {title(row)}
          </h3>
          <p class="mt-1 text-sm text-text-secondary">{row.connectionName}</p>
          <p class="mt-1 flex items-center gap-1.5 text-sm text-text-muted">
            <FolderOpen class="size-3.5" aria-hidden="true" />{row.libraryLabel}
          </p>
        </div>
        <Badge variant="secondary">{stateLabel(row)}</Badge>
      </header>
      {#if row.request.problem}
        <p class="text-sm text-text-muted" role="status">{row.request.problem}</p>
      {/if}
      {#if session.isAdmin}
        <div class="flex flex-wrap items-center gap-2 border-t border-border-subtle pt-4">
          {#if removed && !releasedHoldingIds.includes(row.holding.holdingId)}
            <ManagedHoldingRelease connectionId={row.connectionId} connectionName={row.connectionName}
              holdingId={row.holding.holdingId}
              onaccepted={() => releasedHoldingIds = [...releasedHoldingIds, row.holding.holdingId]} />
          {:else if row.holding.status !== MANAGED_TRACKING_STATUS.released}
            <ManagedHoldingControls connectionId={row.connectionId} connectionName={row.connectionName}
              holdingId={row.holding.holdingId} canPreview={true}
              controlLabel={`${title(row)} settings and activity in ${row.connectionName}`} />
          {/if}
          <a class={buttonVariants({ variant: "ghost", size: "sm" })} href={sourceHref(row)}
            aria-label={`Open ${title(row).toLowerCase()} connected title`}>
            Open connected title<ArrowUpRight aria-hidden="true" />
          </a>
        </div>
      {/if}
    </Panel>
  {/each}
</div>
