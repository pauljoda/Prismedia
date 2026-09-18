<script lang="ts">
  import { ArrowUpRight, FolderOpen } from "@lucide/svelte";
  import { Badge, Panel, buttonVariants } from "@prismedia/ui-svelte";
  import { MANAGED_REQUEST_PHASE, MANAGED_TRACKING_STATUS } from "$lib/api/generated/codes";
  import type { EntityCapabilityExternalLibraryProvenanceCapability } from "$lib/api/generated/model";
  import { getGetPluginIconUrl } from "$lib/api/generated/prismedia";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import ManagedHoldingControls from "./ManagedHoldingControls.svelte";
  import ManagedHoldingRelease from "./ManagedHoldingRelease.svelte";
  import { useSession } from "$lib/stores/session.svelte";

  let { origin, sourceLink, hasSourceMedia = false }: {
    origin: EntityCapabilityExternalLibraryProvenanceCapability;
    sourceLink: { href: string; label: string; ariaLabel: string } | null;
    hasSourceMedia?: boolean;
  } = $props();
  const session = useSession();
  let releasedHoldingId = $state<string | null>(null);
  const phaseLabels = {
    [MANAGED_REQUEST_PHASE.pendingCreation]: "Request queued",
    [MANAGED_REQUEST_PHASE.creationUncertain]: "Checking request",
    [MANAGED_REQUEST_PHASE.awaitingFiles]: "Waiting for files",
    [MANAGED_REQUEST_PHASE.completed]: "Available in Prismedia",
    [MANAGED_REQUEST_PHASE.rejected]: "Needs attention",
    [MANAGED_REQUEST_PHASE.cancelled]: "Cancelled",
    [MANAGED_REQUEST_PHASE.ownershipReleased]: "No longer managed",
    [MANAGED_REQUEST_PHASE.remoteRemoved]: "Removed from source",
  };
  const removed = $derived(origin.request?.phase === MANAGED_REQUEST_PHASE.remoteRemoved
    || origin.holding?.status === MANAGED_TRACKING_STATUS.removed);
  const waiting = $derived(origin.request && !removed && origin.request.phase !== MANAGED_REQUEST_PHASE.completed);
</script>

<Panel class="w-full max-w-2xl space-y-5 p-5">
  <header class="flex flex-wrap items-center gap-3">
    <PluginIcon name={origin.connectionName} iconUrl={getGetPluginIconUrl(origin.pluginId)} class="size-9" />
    <div class="min-w-0 flex-1">
      <h3 class="text-base font-semibold">{origin.connectionName}</h3>
      <p class="mt-1 flex items-center gap-1.5 text-sm text-text-muted"><FolderOpen class="size-3.5" />{origin.libraryLabel}</p>
    </div>
    {#if removed}<Badge variant="secondary">Removed from source</Badge>
    {:else if origin.request}<Badge variant="secondary">{phaseLabels[origin.request.phase]}</Badge>{/if}
  </header>
  <p class="text-sm leading-relaxed text-text-secondary">
    {#if removed && hasSourceMedia}
      This title was removed from {origin.connectionName}. Files still available to Prismedia remain in your library.
    {:else if removed}
      This title was removed from {origin.connectionName}. Prismedia retained its metadata and request history.
    {:else if waiting}
      {origin.connectionName} handles this request. When its files are ready, you can open them here.
    {:else}
      Files stay managed by {origin.connectionName}. Prismedia reads them in place.
    {/if}
  </p>
  {#if origin.request?.problem}
    <p class="text-sm text-text-muted" role="status">{origin.request.problem}</p>
  {/if}
  {#if session.isAdmin}
    <div class="flex flex-wrap items-center gap-2 border-t border-border-subtle pt-4">
      {#if origin.holding && origin.holding.status === MANAGED_TRACKING_STATUS.removed
        && releasedHoldingId !== origin.holding.holdingId}
        <ManagedHoldingRelease connectionId={origin.connectionId} connectionName={origin.connectionName}
          holdingId={origin.holding.holdingId} onaccepted={() => releasedHoldingId = origin.holding!.holdingId} />
      {:else if origin.holding && origin.holding.status !== MANAGED_TRACKING_STATUS.released}
        <ManagedHoldingControls connectionId={origin.connectionId} connectionName={origin.connectionName}
          holdingId={origin.holding.holdingId} canPreview={true} />
      {/if}
      {#if sourceLink}
        <a class={buttonVariants({ variant: "ghost", size: "sm" })} href={sourceLink.href} aria-label={sourceLink.ariaLabel}>
          {sourceLink.label}<ArrowUpRight aria-hidden="true" />
        </a>
      {/if}
    </div>
  {/if}
</Panel>
