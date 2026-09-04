<script lang="ts">
  import { Badge as UiBadge } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import {
    BookOpen,
    BookOpenText,
    CloudDownload,
    Images,
    Info,
    LibraryBig,
    Play,
    SlidersHorizontal,
    Users,
  } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import MediaProgressPanel from "$lib/components/MediaProgressPanel.svelte";
  import ComicPageThumbnailGrid from "$lib/components/comics/ComicPageThumbnailGrid.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import { getCapability, isWanted } from "$lib/api/capabilities";
  import { updateEntityProgress } from "$lib/api/consumption";
  import { fetchEntityReaderManifest } from "$lib/api/entity-reader";
  import {
    fetchEntity,
    fetchEntityChildReferences,
    type EntityCardFull,
  } from "$lib/api/entities";
  import { getChildIds } from "$lib/entities/entity-children";
  import {
    entityCardToDetailCard,
    type EntityDetailCardFull,
    type EntityDetailCredit,
    type EntityDetailTag,
  } from "$lib/entities/entity-detail";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import type { EntityReaderManifestResponse } from "$lib/api/generated/model";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
  import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";
  import {
    CAPABILITY_KIND,
    CREDIT_ROLE,
    ENTITY_KIND,
  } from "$lib/entities/entity-codes";
  import { PROGRESS_UNIT } from "$lib/api/generated/codes";
  import type { AppBreadcrumb } from "$lib/stores/app-chrome.svelte";
  import { numberValue } from "$lib/utils/format";

  interface Props {
    entityId: string;
    seriesId?: string | null;
  }

  let { entityId, seriesId = null }: Props = $props();

  let parentSeries = $state.raw<EntityCardFull | null>(null);
  let parentVolume = $state.raw<EntityCardFull | null>(null);
  let volumeCards = $state<EntityThumbnailCard[]>([]);
  let installmentCards = $state<EntityThumbnailCard[]>([]);
  let allInstallmentCards = $state<EntityThumbnailCard[]>([]);
  let pageManifest = $state.raw<EntityReaderManifestResponse | null>(null);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let progressBusy = $state(false);

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => `${seriesId ?? "root"}:${entityId}`,
    load: ({ signal }) => loadComicEntity(signal),
    breadcrumbs: (nextEntity) => {
      const crumbs: AppBreadcrumb[] = [{ label: "Comics", href: "/comics" }];
      if (nextEntity.kind !== ENTITY_KIND.comicSeries && parentSeries) {
        crumbs.push({ label: parentSeries.title, href: `/comics/${parentSeries.id}` });
      }
      if (nextEntity.kind === ENTITY_KIND.comicInstallment && parentVolume) {
        crumbs.push({
          label: parentVolume.title,
          href: `/comics/${parentSeries?.id ?? seriesId}/volumes/${parentVolume.id}`,
        });
      }
      crumbs.push({ label: nextEntity.title });
      return crumbs;
    },
  });
  const entity = $derived(detail.entity);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!entity) return null;
    return {
      ...entityCardToDetailCard(entity),
      tags: relationshipTags,
      credits: relationshipCredits,
      studio: relationshipStudio,
    };
  });
  const dates = $derived(card?.dates ?? []);
  const pageSequence = $derived(
    entity ? getCapability(entity.capabilities, CAPABILITY_KIND.pageSequence) : undefined,
  );
  const installmentMetadata = $derived(
    entity ? getCapability(entity.capabilities, CAPABILITY_KIND.comicInstallmentMetadata) : undefined,
  );
  const seriesMetadata = $derived(
    entity ? getCapability(entity.capabilities, CAPABILITY_KIND.seriesMetadata) : undefined,
  );
  const progress = $derived(
    entity ? getCapability(entity.capabilities, CAPABILITY_KIND.progress) : undefined,
  );
  const entityWanted = $derived(!!entity && isWanted(entity.capabilities));
  const readTargetId = $derived.by(() => {
    if (pageSequence) return entity?.id ?? null;
    if (progress?.currentEntityId && allInstallmentCards.some((item) => item.entity.id === progress.currentEntityId)) {
      return progress.currentEntityId;
    }
    return allInstallmentCards[0]?.entity.id ?? null;
  });
  const firstInstallmentId = $derived(allInstallmentCards[0]?.entity.id ?? null);
  const lastInstallmentId = $derived(allInstallmentCards.at(-1)?.entity.id ?? null);
  const completed = $derived(Boolean(progress?.completedAt));
  const progressIndex = $derived(numberValue(progress?.index) ?? 0);
  const progressTotal = $derived(
    pageSequence ? numberValue(pageSequence.pageCount) ?? 0 : allInstallmentCards.length,
  );
  const progressPercent = $derived.by(() => {
    const projected = numberValue(progress?.consumedPercent);
    if (!pageSequence && projected != null) return projected;
    return progressTotal > 0 ? Math.min(100, Math.max(0, (progressIndex / progressTotal) * 100)) : 0;
  });
  const currentInstallment = $derived(
    allInstallmentCards.find((item) => item.entity.id === progress?.currentEntityId) ?? null,
  );
  const progressPositionLabel = $derived(
    pageSequence
      ? progressTotal > 0 ? `Page ${Math.min(progressIndex + 1, progressTotal)} of ${progressTotal}` : null
      : currentInstallment?.entity.title ?? null,
  );
  const progressCountLabel = $derived(
    pageSequence
      ? installmentKindLabel(installmentMetadata?.installmentKind)
      : `${allInstallmentCards.length} release${allInstallmentCards.length === 1 ? "" : "s"}`,
  );
  const hasProgressSurface = $derived(Boolean(pageSequence || allInstallmentCards.length > 0));

  const identifyAction = useIdentifyDetailAction(() => entity);
  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    const actions: EntityDetailActionButton[] = [];
    if (readTargetId) {
      actions.push({
        id: "read",
        label: completed ? "Re-read" : progress?.currentEntityId ? "Resume" : "Read",
        icon: pageSequence ? BookOpenText : Play,
        variant: "primary",
        onClick: () => openReader(readTargetId, completed),
      });
    }
    if (identifyAction.action) actions.push(identifyAction.action);
    return actions;
  });

  const acq = useEntityAcquisition({
    entityId: () => entity?.id,
    capabilities: () => entity?.capabilities,
    childCards: () => requestableDirectChildCards(
      entity?.id,
      [...volumeCards, ...installmentCards],
    ),
    onChanged: refreshEntity,
    onStatusChanged: refreshEntity,
    onPruned: () => goto(entity?.kind === ENTITY_KIND.comicSeries ? "/comics" : `/comics/${parentSeries?.id ?? seriesId}`),
  });
  const wantedStateLabel = $derived(acquisitionStatusDisplay(acq.acquisition?.summary.status).label);
  const fileManagement = {
    onDeleted: () => goto(entity?.kind === ENTITY_KIND.comicSeries ? "/comics" : `/comics/${parentSeries?.id ?? seriesId}`),
    onReverted: () => refreshAfterManagedFileRevert(acq, refreshEntity),
  };

  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "credits", label: "Creators", icon: Users },
    { id: "acquisition" },
  ]);
  const detailTabs = $derived.by((): EntityDetailTab[] => [
    {
      id: "details",
      label: "Details",
      icon: Info,
      sections: ["description", "tags", "studio", "credits"],
    },
    {
      id: "metadata",
      label: "Metadata",
      icon: SlidersHorizontal,
      sections: ["stats", "dates", "positions", "technical", "source", "links"],
      layout: "grid",
    },
    ...(acq.visible
      ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
      : []),
  ]);

  async function loadComicEntity(signal: AbortSignal): Promise<EntityCardFull> {
    const nextEntity = await fetchEntity(entityId, { signal });
    const nextPageSequence = getCapability(nextEntity.capabilities, CAPABILITY_KIND.pageSequence);
    const nextSeries = nextEntity.kind === ENTITY_KIND.comicSeries
      ? nextEntity
      : await fetchEntity(seriesId ?? nextEntity.parentEntityId ?? "", { signal });
    const nextVolume = nextEntity.kind === ENTITY_KIND.comicInstallment
      && nextEntity.parentEntityId
      && nextEntity.parentEntityId !== nextSeries.id
      ? await fetchEntity(nextEntity.parentEntityId, { signal })
      : nextEntity.kind === ENTITY_KIND.comicVolume ? nextEntity : null;

    const volumeIds = getChildIds(nextEntity, ENTITY_KIND.comicVolume);
    const directInstallmentIds = getChildIds(nextEntity, ENTITY_KIND.comicInstallment);
    const nestedGroups = volumeIds.length > 0
      ? await fetchEntityChildReferences(volumeIds, { signal })
      : [];
    const nestedByParent = new Map(nestedGroups.map((group) => [group.parentId, group.items]));
    const nestedInstallmentIds = volumeIds.flatMap((volumeId) =>
      (nestedByParent.get(volumeId) ?? [])
        .filter((item) => item.kind === ENTITY_KIND.comicInstallment)
        .map((item) => item.id),
    );
    const allInstallmentIds = [...new Set([...directInstallmentIds, ...nestedInstallmentIds])];
    const [volumes, directInstallments, allInstallments, relationships, nextPageManifest] = await Promise.all([
      fetchOrderedEntityThumbnails(volumeIds, { signal }),
      fetchOrderedEntityThumbnails(directInstallmentIds, { signal }),
      fetchOrderedEntityThumbnails(allInstallmentIds, { signal }),
      hydrateStandardRelationshipCards(nextEntity, { signal }),
      nextPageSequence
        ? fetchEntityReaderManifest(nextEntity.id, { signal })
        : Promise.resolve(null),
    ]);
    signal.throwIfAborted();
    const installmentById = new Map(allInstallments.map((item) => [item.id, item]));
    const orderedInstallments = allInstallmentIds.flatMap((id) => {
      const item = installmentById.get(id);
      return item ? [item] : [];
    });

    parentSeries = nextSeries;
    parentVolume = nextVolume;
    volumeCards = thumbnailsToCards(volumes, {
      hrefFor: (thumbnail) => `/comics/${nextSeries.id}/volumes/${thumbnail.id}`,
    });
    installmentCards = thumbnailsToCards(directInstallments, {
      hrefFor: (thumbnail) => `/comics/${nextSeries.id}/installments/${thumbnail.id}`,
    });
    allInstallmentCards = thumbnailsToCards(orderedInstallments, {
      hrefFor: (thumbnail) => `/comics/${nextSeries.id}/installments/${thumbnail.id}`,
    });
    pageManifest = nextPageManifest;
    relationshipCredits = relationships.credits;
    relationshipStudio = relationships.studio;
    relationshipTags = relationships.relationshipTags;
    return nextEntity;
  }

  function refreshEntity(): Promise<void> {
    return detail.reload({ showLoading: false });
  }

  function openReader(targetId: string, reset = false) {
    const returnTo = comicReturnHref();
    const params = new URLSearchParams({ returnTo });
    if (reset) params.set("reset", "1");
    void goto(`/entities/${targetId}/reader?${params}`);
  }

  function comicReturnHref(): string {
    return entity?.kind === ENTITY_KIND.comicInstallment
      ? `/comics/${parentSeries?.id ?? seriesId}/installments/${entity.id}`
      : entity?.kind === ENTITY_KIND.comicVolume
        ? `/comics/${parentSeries?.id ?? seriesId}/volumes/${entity.id}`
        : `/comics/${entity?.id ?? seriesId}`;
  }

  async function toggleCompleted(nextCompleted: boolean) {
    if (!entity || progressBusy) return;
    const currentId = pageSequence ? entity.id : nextCompleted ? lastInstallmentId : readTargetId;
    if (!currentId) return;
    progressBusy = true;
    try {
      await updateEntityProgress(entity.id, {
        currentEntityId: currentId,
        unit: pageSequence ? PROGRESS_UNIT.page : PROGRESS_UNIT.item,
        index: nextCompleted ? progressTotal : progressIndex,
        total: progressTotal,
        mode: pageSequence?.defaultMode ?? progress?.mode,
        completed: nextCompleted,
      });
      await refreshEntity();
    } finally {
      progressBusy = false;
    }
  }

  function startOver() {
    const target = pageSequence ? entity?.id : firstInstallmentId;
    if (target) openReader(target, true);
  }

  function installmentKindLabel(kind: string | null | undefined): string | null {
    if (!kind) return null;
    return kind.replaceAll("-", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
  }
</script>

<svelte:head>
  <title>{entity?.title ?? "Comic"} · Prismedia</title>
</svelte:head>

<div class="comic-detail-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load comic details."
    onRetry={detail.retry}
  >
    {#if card && entity}
      <EntityDetail
        {card}
        wantedStatus={acq.acquisition?.summary.status ?? null}
        onRatingChange={detail.changeRating}
        onFavoriteToggle={detail.toggleFavorite}
        onOrganizedToggle={detail.toggleOrganized}
        onMetadataSave={detail.saveMetadata}
        ratingBusy={detail.ratingBusy}
        posterSize="large"
        tabs={detailTabs}
        sections={detailSections}
        actionButtons={heroActions}
        peopleLabel="Creators"
        defaultCreditRole={CREDIT_ROLE.writer}
      >
        {#snippet heroMeta()}
          {#if entity.kind !== ENTITY_KIND.comicSeries && parentSeries}
            <span class="meta-item is-parent">{parentSeries.title}</span>
          {/if}
          {#if entity.kind === ENTITY_KIND.comicInstallment && parentVolume}
            <span class="meta-sep"></span>
            <span class="meta-item">{parentVolume.title}</span>
          {/if}
          <EntityDetailHeroDates
            {dates}
            leadingSeparator={entity.kind !== ENTITY_KIND.comicSeries && Boolean(parentSeries)}
          />
        {/snippet}

        {#snippet heroBadges()}
          {#if entityWanted}
            <UiBadge variant="outline">{wantedStateLabel}</UiBadge>
          {/if}
          {#if installmentMetadata}
            <UiBadge variant="outline">{installmentKindLabel(installmentMetadata.installmentKind)}</UiBadge>
          {/if}
          {#if seriesMetadata?.status}
            <UiBadge variant="outline">{seriesMetadata.status}</UiBadge>
          {/if}
          {#if pageSequence}
            <UiBadge variant="outline">{numberValue(pageSequence.pageCount) ?? 0} pages</UiBadge>
          {/if}
        {/snippet}

        {#snippet sectionContent(section)}
          {#if section.id === "acquisition"}
            <EntityAcquisitionCard
              {acq}
              {entity}
              {fileManagement}
              onCancelled={refreshEntity}
              onImported={refreshEntity}
            />
          {/if}
        {/snippet}
      </EntityDetail>

      {#if hasProgressSurface}
        <section class="progress-section">
          <MediaProgressPanel
            kind="read"
            {completed}
            percent={progressPercent}
            positionLabel={progressPositionLabel}
            countLabel={progressCountLabel}
            canResume={Boolean(readTargetId) && !completed}
            canStartOver={Boolean(firstInstallmentId || pageSequence)}
            busy={progressBusy}
            resumeLabel={pageSequence ? "Resume" : "Continue"}
            onToggleCompleted={toggleCompleted}
            onResume={() => readTargetId && openReader(readTargetId)}
            onStartOver={startOver}
          />
        </section>
      {/if}

      {#if pageManifest && pageManifest.pages.length > 0}
        <EntityGridSection
          title="Pages"
          count={pageManifest.pages.length}
          icon={Images}
          prefsKey={`comic-${entity.id}-pages-section`}
        >
          <ComicPageThumbnailGrid
            entityId={entity.id}
            entityTitle={entity.title}
            pages={pageManifest.pages}
            returnHref={comicReturnHref()}
          />
        </EntityGridSection>
      {/if}

      {#if volumeCards.length > 0}
        <EntityGridSection
          title="Volumes"
          count={volumeCards.length}
          icon={LibraryBig}
          prefsKey={`comic-${entity.id}-volumes-section`}
        >
          <EntityGrid
            cards={volumeCards}
            prefsKey={`comic-${entity.id}-volumes`}
            initialSortBy="position"
            emptyTitle="No volumes"
            emptyMessage="This comic has no collected volumes."
          />
        </EntityGridSection>
      {/if}

      {#if installmentCards.length > 0}
        <EntityGridSection
          title={volumeCards.length > 0 ? "Uncollected releases" : "Releases"}
          count={installmentCards.length}
          icon={BookOpen}
          prefsKey={`comic-${entity.id}-installments-section`}
        >
          <EntityGrid
            cards={installmentCards}
            prefsKey={`comic-${entity.id}-installments`}
            initialSortBy="position"
            emptyTitle="No releases"
            emptyMessage="No released installments are available yet."
          />
        </EntityGridSection>
      {/if}

      {#if entity.kind !== ENTITY_KIND.comicInstallment && volumeCards.length === 0 && installmentCards.length === 0}
        <div class="empty-children">
          <p>No released installments are linked yet.</p>
        </div>
      {/if}
    {/if}
  </EntityDetailPageState>
</div>

<style>
  .comic-detail-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
  }

  :global(.meta-item) {
    white-space: nowrap;
    font-size: 0.82rem;
  }

  :global(.meta-item.is-parent) {
    color: var(--color-text-accent, #c7c9cc);
  }

  :global(.meta-sep) {
    display: inline-block;
    width: 3px;
    height: 3px;
    margin: 0 0.5rem;
    border-radius: 50%;
    background: var(--color-text-muted, #8a93a6);
    opacity: 0.5;
  }

  .progress-section {
    max-width: 40rem;
  }

  .empty-children {
    display: grid;
    min-height: 8rem;
    place-items: center;
    border: 1px dashed var(--color-border-default);
    border-radius: var(--radius-md);
    color: var(--color-text-muted);
  }

  .empty-children p {
    margin: 0;
  }
</style>
