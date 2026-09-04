<script lang="ts">
  /**
   * THE acquisition + monitoring surface an entity page mounts — the Acquisition detail tab's body,
   * collapsing everything the request layer knows about an entity: its stable monitor,
   * the wanted placeholder's "Search for release", direct-child monitoring, and the full acquisition
   * management panel (releases, live download, files, cancel). All state lives in the
   * page-owned {@link useEntityAcquisition} composable, whose `visible` also gates the tab itself;
   * this component only renders it. Renders nothing while the state says there is no story.
   */
  import { RefreshCw, Search, Wrench } from "@lucide/svelte";
  import { Button, Disclosure } from "@prismedia/ui-svelte";
  import { ACQUISITION_STATUS, ENTITY_KIND, ENTITY_KIND_DEFINITIONS } from "$lib/api/generated/codes";
  import type { EntityCapability } from "$lib/api/generated/model";
  import AcquisitionPanel from "$lib/components/acquisitions/AcquisitionPanel.svelte";
  import ManualAcquisitionActions from "$lib/components/acquisitions/ManualAcquisitionActions.svelte";
  import EntityChildMonitoring from "$lib/components/acquisitions/EntityChildMonitoring.svelte";
  import type { EntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { acquisitionStatusShouldPoll } from "$lib/requests/acquisition-status";
  import EntityFileManagementAction from "$lib/components/entities/EntityFileManagementAction.svelte";
  import type { EntityFileManagementCallbacks } from "$lib/entities/entity-file-management";
  import EntityBlocklistClearAction from "$lib/components/acquisitions/EntityBlocklistClearAction.svelte";
  import EntityMonitorControl from "$lib/components/acquisitions/EntityMonitorControl.svelte";
  import {
    requestKindForEntityKind,
    requestKindInfo,
  } from "$lib/requests/request-helpers";
  import { isEntityKindCode } from "$lib/entities/entity-codes";

  let {
    acq,
    entity,
    fileManagement,
    showEntityRequestControls = true,
    showAcquisitionPanel = true,
    onCancelled,
    onImported,
  }: {
    /** The page-owned acquisition state (from {@link useEntityAcquisition}). */
    acq: EntityAcquisition;
    /** Entity core projected into the shared managed-file action. */
    entity?: { id: string; title: string; kind?: string; capabilities: EntityCapability[] } | null;
    /** Route follow-ups after managed deletion either removes or reverts the Entity. */
    fileManagement?: EntityFileManagementCallbacks;
    /** False when an owner-specific surface provides its own monitor and request controls. */
    showEntityRequestControls?: boolean;
    /** False when a richer owner-specific surface renders the acquisition rows itself. */
    showAcquisitionPanel?: boolean;
    /**
     * Called after the acquisition is cancelled, so the page can refresh. Cancel stops the download
     * only — the wanted placeholder and any monitoring stay, and the page keeps existing.
     */
    onCancelled?: () => void;
    /** Called once when the acquisition becomes Imported so the page can refresh its Entity in place. */
    onImported?: () => void | Promise<void>;
  } = $props();

  const hasActions = $derived(
    acq.showSync ||
      (showEntityRequestControls && acq.showSearch) ||
      acq.showSearchMissing ||
      (acq.showFileManagement && Boolean(entity && fileManagement)),
  );
  const monitorExpanded = $derived(
    !showEntityRequestControls
      || !acq.showMonitor
      || acq.monitorActive
      || acq.monitorStopping
      || acq.monitorDeletingFiles,
  );
  const activeChildAcquisitionCount = $derived(acq.childCards.filter((card) =>
    acquisitionStatusShouldPoll(card.wantedStatus)
    || acquisitionStatusShouldPoll(card.latestAcquisitionStatus),
  ).length);
  const failedParentWithChildActivity = $derived(
    acq.acquisition?.summary.status === ACQUISITION_STATUS.failed
      && activeChildAcquisitionCount > 0,
  );
  const kindDefinition = $derived(
    entity?.kind && isEntityKindCode(entity.kind)
      ? ENTITY_KIND_DEFINITIONS[entity.kind]
      : null,
  );
  const replaceableKind = $derived(kindDefinition?.manualAcquisition.supportsReplacement === true);
  const uploadableAcquisitionKind = $derived(
    kindDefinition?.manualAcquisition.supportsUpload === true,
  );
  const monitorKindInfo = $derived.by(() => {
    const kind = entity?.kind ? requestKindForEntityKind(entity.kind) : null;
    return kind ? requestKindInfo(kind) : null;
  });
  const hasImportedBaseline = $derived(
    acq.acquisition?.summary.status === ACQUISITION_STATUS.imported,
  );
  const hasOwnedContent = $derived(acq.showFileManagement || hasImportedBaseline);
  const activeChildLabel = $derived(
    acq.childCards.every((card) => card.entity.kind === ENTITY_KIND.videoEpisode)
      ? activeChildAcquisitionCount === 1 ? "episode" : "episodes"
      : acq.childCards.every((card) => card.entity.kind === ENTITY_KIND.audioTrack)
        ? activeChildAcquisitionCount === 1 ? "track" : "tracks"
      : activeChildAcquisitionCount === 1 ? "child item" : "child items",
  );
</script>

{#if acq.visible}
  <section class="acquisition-card">
    {#if !monitorExpanded && acq.showFileManagement && entity && fileManagement}
      <div class="flex justify-end">
        <EntityFileManagementAction
          {entity}
          onDeleted={fileManagement.onDeleted}
          onReverted={fileManagement.onReverted}
          compact
        />
      </div>
    {/if}

    {#if showEntityRequestControls && acq.showMonitor}
      <EntityMonitorControl {acq} kindInfo={monitorKindInfo} />
    {/if}

    {#if acq.monitorError}
      <p role="alert" class="text-[0.72rem] text-error-text">{acq.monitorError}</p>
    {/if}

    {#if monitorExpanded}
      {#if hasActions}
      <div class="flex flex-wrap items-center gap-2">
        {#if acq.showSync}
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={acq.syncBusy}
            onclick={() => void acq.syncNow()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title="Re-sync from the provider now instead of waiting for the daily sweep"
          >
            <RefreshCw class="h-3.5 w-3.5" />
            {acq.syncBusy ? "Checking…" : "Check for new works"}
          </Button>
        {/if}
        {#if showEntityRequestControls && acq.showSearch}
          <Button
            type="button"
            variant="primary"
            size="sm"
            disabled={acq.searchBusy}
            onclick={() => void acq.searchForRelease()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
          >
            <Search class="h-3.5 w-3.5" />
            {acq.searchBusy ? "Searching…" : "Search for release"}
          </Button>
        {/if}
        {#if acq.showSearchMissing}
          <Button
            type="button"
            variant="primary"
            size="sm"
            disabled={acq.missingBusy}
            onclick={() => void acq.searchMissing()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title="Sweep for anything missing at any depth — every gap gets its own monitored search"
          >
            <Search class="h-3.5 w-3.5" />
            {acq.missingBusy
              ? "Searching…"
              : acq.missingChildCount > 0
                ? `Search ${acq.missingChildCount} missing`
                : "Search missing content"}
          </Button>
        {/if}
        {#if acq.showFileManagement && entity && fileManagement}
          <EntityFileManagementAction
            {entity}
            onDeleted={fileManagement.onDeleted}
            onReverted={fileManagement.onReverted}
            compact
          />
        {/if}
      </div>
      {/if}

      {#if acq.missingResult}
        <p class="text-[0.72rem] text-text-muted">{acq.missingResult}</p>
      {/if}

      {#if showEntityRequestControls && acq.showSearch}
        <p class="text-[0.72rem] text-text-muted">
          No file yet. Searching starts an auto-grabbing, monitored acquisition for this item.
        </p>
      {/if}

      {#if entity}
        <Disclosure title="More acquisition actions" icon={Wrench}>
          <div class="flex flex-col gap-3">
            {#if uploadableAcquisitionKind}
              <ManualAcquisitionActions
                entityId={entity.id}
                canReplace={hasOwnedContent && replaceableKind}
                canUpload={Boolean(acq.acquisition) || (hasOwnedContent && replaceableKind)}
                onStarted={async (detail) => {
                  acq.setAcquisition(detail);
                  await acq.refresh();
                }}
              />
            {/if}
            <EntityBlocklistClearAction entityId={entity.id} entityTitle={entity.title} />
          </div>
        </Disclosure>
      {/if}

      {#if acq.childCards.length > 0}
        <EntityChildMonitoring
          cards={acq.childCards}
          onChanged={acq.childMonitoringChanged}
        />
      {/if}

      {#if showAcquisitionPanel && acq.acquisition}
        {#if failedParentWithChildActivity}
          <Disclosure
            title={`Parent release attempt failed · ${activeChildAcquisitionCount} ${activeChildLabel} active instead`}
          >
            <div>
              {#key acq.acquisition.summary.id}
                <AcquisitionPanel
                  acquisitionId={acq.acquisition.summary.id}
                  detail={acq.acquisition}
                  onDetailChange={acq.setAcquisition}
                  {onCancelled}
                  {onImported}
                  onReset={acq.refresh}
                />
              {/key}
            </div>
          </Disclosure>
        {:else}
            {#key acq.acquisition.summary.id}
              <AcquisitionPanel
                acquisitionId={acq.acquisition.summary.id}
                detail={acq.acquisition}
                onDetailChange={acq.setAcquisition}
                {onCancelled}
                {onImported}
                onReset={acq.refresh}
              />
            {/key}
        {/if}
      {/if}
    {/if}
  </section>

{/if}

<style>
  /* Frameless: the detail tab panel supplies the surface, border, and padding. */
  .acquisition-card {
    display: grid;
    gap: 0.9rem;
    min-width: 0;
  }
</style>
