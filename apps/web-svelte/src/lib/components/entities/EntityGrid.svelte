<script lang="ts">
  import { browser } from "$app/environment";
  import { onMount } from "svelte";
  import { SvelteMap } from "svelte/reactivity";
  import { isNsfw, isWanted, withFlagCapability } from "$lib/api/capabilities";
  import { removeWantedEntities } from "$lib/api/requests";
  import type { EntityCapability } from "$lib/api/generated/model";
  import { updateEntityFlags } from "$lib/api/entity-mutations";
  import { createFilterPresets, type FilterPreset } from "$lib/filter-presets";
  import { createEntityGridPrefs, type EntityGridPrefs } from "$lib/entities/entity-grid-prefs";
  import { usePageSnapshots } from "$lib/stores/page-snapshots.svelte";
  import {
    ENTITY_GRID_ALL_KINDS,
    applyEntityGridState,
    buildCapabilityFilterOptions,
    buildEntityKindTabs,
    entityGridRequestFromState,
    type EntityGridRequest,
    type EntityGridSort,
    type EntityGridSortDir,
    type EntityGridViewMode,
    type EntityGridBulkAction,
  } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { isCollectionEntityType, type CollectionEntityType } from "$lib/collections/models";
  import EntityGridFilterDrawer from "./EntityGridFilterDrawer.svelte";
  import EntityGridContent from "./EntityGridContent.svelte";
  import EntityGridPagination from "./EntityGridPagination.svelte";
  import EntityGridTabs from "./EntityGridTabs.svelte";
  import EntityGridToolbar from "./EntityGridToolbar.svelte";
  import {
    createEntityGridPreset,
    createEntityGridPresetId,
    entityGridPresetStorageKey,
    readEntityGridPreset,
  } from "./entity-grid-filter-presets";
  import {
    EntityGridPaginationController,
    normalizeEntityGridPageSize,
  } from "./entity-grid-pagination-controller.svelte";
  import { EntityGridViewportController } from "./entity-grid-viewport-controller.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import type { NsfwMode } from "$lib/nsfw/cookie";

  const DEFAULT_PAGE_SIZE = 100;
  const DEFAULT_PAGE_SIZE_OPTIONS = [100, 250, 500, 1000];
  const DEFAULT_DESKTOP_SCALE = 6;
  const MOBILE_THUMBNAIL_QUERY = "(max-width: 639.98px)";

  interface Props {
    bulkActions?: EntityGridBulkAction[];
    /**
     * Whether the selection bar offers the library-wide built-in bulk actions (Add to Collection, Mark
     * NSFW). Disable for non-library grids (e.g. the request queue of synthetic entities) so only the
     * custom {@link bulkActions} are offered.
     */
    bulkLibraryActions?: boolean;
    cards: EntityThumbnailCard[];
    emptyMessage?: string;
    emptyTitle?: string;
    /**
     * The single entity kind this grid is browsing, when known. Drives adaptive
     * filter labels (e.g. Read/Unread for books vs Watched/Unwatched for video).
     */
    entityKind?: string;
    /**
     * When true, the toolbar exposes a vertical "feed" view mode (single-column,
     * content sized to its aspect, with inline autoplay for video-capable items).
     * Only meaningful for image/gallery routes.
     */
    enableFeedView?: boolean;
    /**
     * Hide the book type/format filter chips. Used by constrained book sub-views
     * (Comics/eBooks) that already lock book type/format, where exposing the chips
     * would be redundant and confusing.
     */
    lockBookFilters?: boolean;
    hasMore?: boolean;
    initialPageSize?: number;
    initialMediaWall?: boolean;
    initialSortBy?: EntityGridSort;
    initialSortDir?: EntityGridSortDir;
    dockControls?: boolean;
    loading?: boolean;
    loadingMore?: boolean;
    loadMoreError?: string | null;
    maxScale?: number;
    minScale?: number;
    nsfwMode?: NsfwMode;
    onCardActivate?: (card: EntityThumbnailCard, visibleCards: EntityThumbnailCard[]) => void;
    onLoadMore?: () => void | Promise<void>;
    onPageSizeChange?: (pageSize: number) => void;
    onRequestChange?: (request: EntityGridRequest) => void;
    onRenderedCountChange?: (renderedCount: number) => void;
    onSelectionChange?: (selectedIds: string[]) => void;
    pageSizeOptions?: number[];
    prefsKey?: string;
    /**
     * Server-reported total number of entities matching the active filters, ignoring
     * the cursor. When provided the pagination strip uses it for the readout total,
     * `page X of Y` indicator, and seek-to-end target. Falls back to the locally
     * loaded card count when omitted.
     */
    remoteTotalCount?: number | null;
    selectable?: boolean;
    showPagination?: boolean;
    scrollBottomPadding?: number;
    scrollMaxHeight?: string | null | undefined;
    scrollMinHeight?: number;
    /** Start with selection mode enabled so a plain card click toggles selection. */
    initialSelectionActive?: boolean;
    /** When false, cards render as non-link surfaces unless an explicit activation handler is provided. */
    cardLinks?: boolean;
  }

  let {
    bulkActions = [],
    bulkLibraryActions = true,
    cards,
    emptyMessage = "Try adjusting your search or filters.",
    emptyTitle = "Nothing present",
    entityKind,
    lockBookFilters = false,
    hasMore = false,
    initialPageSize = DEFAULT_PAGE_SIZE,
    initialMediaWall = false,
    initialSortBy = "title",
    initialSortDir = "asc",
    dockControls = true,
    enableFeedView = false,
    loading = false,
    loadingMore = false,
    loadMoreError = null,
    maxScale = 12,
    minScale = 2,
    nsfwMode,
    onCardActivate,
    onLoadMore,
    onPageSizeChange,
    onRequestChange,
    onRenderedCountChange,
    onSelectionChange,
    pageSizeOptions = DEFAULT_PAGE_SIZE_OPTIONS,
    prefsKey,
    remoteTotalCount = null,
    selectable = true,
    showPagination = true,
    scrollBottomPadding = 24,
    scrollMaxHeight = undefined,
    scrollMinHeight = 320,
    initialSelectionActive = false,
    cardLinks = true,
  }: Props = $props();

  function isMobileThumbnailViewport(): boolean {
    return browser &&
      typeof window.matchMedia === "function" &&
      window.matchMedia(MOBILE_THUMBNAIL_QUERY).matches;
  }

  function defaultScale(): number {
    // On phones the very largest thumbnails (minScale) can feel oversized, so
    // start one step in while still leaning large for touch. Desktop starts at
    // the preferred mid-range density shared by the main library grids.
    return isMobileThumbnailViewport() ? clampScale(minScale + 1) : clampScale(DEFAULT_DESKTOP_SCALE);
  }

  function clampScale(value: number): number {
    return Math.min(maxScale, Math.max(minScale, value));
  }

  // localStorage-backed view-state store for this grid, built once from the
  // stable prefsKey. Dropping an EntityGrid on any page with a prefsKey makes its
  // filters, sort, card size, media wall, page size, and active preset persist
  // across reloads — scoped to the device, with no cross-device sync layer.
  // svelte-ignore state_referenced_locally
  const prefsStore = browser && prefsKey
    ? createEntityGridPrefs(prefsKey, {
        sortBy: initialSortBy,
        sortDir: initialSortDir,
        mediaWall: initialMediaWall,
        scale: defaultScale(),
        pageSize: normalizeEntityGridPageSize(initialPageSize),
      })
    : null;
  const persistedPrefs: EntityGridPrefs | null = prefsStore ? prefsStore.load() : null;

  const capabilityOverrides = new SvelteMap<string, EntityCapability[]>();
  /** Wanted placeholders the server confirmed removed during this session. */
  let removedIds = $state(new Set<string>());
  /** Actionable durable-teardown failures for wanted cards that remain selected. */
  let removeWantedErrors = $state<string[]>([]);
  let activeKind = $state(persistedPrefs?.activeKind ?? ENTITY_GRID_ALL_KINDS);
  let activePresetId = $state<string | null>(persistedPrefs?.activePresetId ?? null);
  let drawerOpen = $state(false);
  let filterIds = $state<string[]>(persistedPrefs?.filterIds ?? []);
  let includeNsfw = $state(persistedPrefs?.includeNsfw ?? true);
  let presets = $state<FilterPreset[]>([]);
  let query = $state(persistedPrefs?.query ?? "");
  let scale = $state(persistedPrefs ? clampScale(persistedPrefs.scale) : defaultScale());
  // svelte-ignore state_referenced_locally
  let mediaWall = $state(persistedPrefs?.mediaWall ?? initialMediaWall);
  let selectedIds = $state<string[]>([]);
  // Selection is explicit by default: until the user turns it on, cards behave as plain links/activators
  // (a single tap navigates). Some focused flows, such as Identify's batch picker, start in selection
  // mode and turn the whole card surface into the checkbox.
  // svelte-ignore state_referenced_locally
  let selectionActive = $state(initialSelectionActive);
  // svelte-ignore state_referenced_locally
  let sortBy = $state<EntityGridSort>(persistedPrefs?.sortBy ?? initialSortBy);
  // svelte-ignore state_referenced_locally
  let sortDir = $state<EntityGridSortDir>(persistedPrefs?.sortDir ?? initialSortDir);
  // Seed for the random sort. Regenerated each time Random is (re)selected so the
  // shuffle changes, but held stable across pagination within one shuffle.
  let randomSeed = $state(1);
  // Fall back to grid when a persisted "feed" preference lands on a route that does
  // not offer the feed toggle, so a stale device pref can't strand the view.
  // svelte-ignore state_referenced_locally
  let viewMode = $state<EntityGridViewMode>(
    persistedPrefs?.viewMode === "feed" && !enableFeedView ? "grid" : (persistedPrefs?.viewMode ?? "grid"),
  );
  // Manual collapse of the secondary toolbar rows, persisted per grid like the
  // rest of the view state. Scroll-driven collapse is handled inside the toolbar
  // and never written here.
  let barsCollapsed = $state(persistedPrefs?.barsCollapsed ?? false);
  const nsfw = useNsfw();
  const effectiveNsfwMode = $derived(nsfwMode ?? nsfw.mode);

  const gridState = $derived({
    activeKind,
    filterIds,
    includeNsfw: effectiveNsfwMode === "show" && includeNsfw,
    query,
    sortBy,
    sortDir,
    randomSeed,
  });
  const effectiveCards = $derived.by(() => {
    const present = removedIds.size === 0 ? cards : cards.filter((c) => !removedIds.has(c.entity.id));
    if (capabilityOverrides.size === 0) return present;
    return present.map((c) => {
      const overridden = capabilityOverrides.get(c.entity.id);
      if (!overridden) return c;
      return { ...c, entity: { ...c.entity, capabilities: overridden } };
    });
  });
  const tabs = $derived(buildEntityKindTabs(effectiveCards, { includeNsfw: gridState.includeNsfw }));
  const filterOptions = $derived(buildCapabilityFilterOptions(effectiveCards, entityKind));
  const visibleCards = $derived(
    applyEntityGridState(effectiveCards, gridState, filterOptions, {
      preserveServerResolvedSorts: Boolean(onRequestChange),
      serverResolvedFilters: Boolean(onRequestChange),
    }),
  );
  const viewport = new EntityGridViewportController({
    dockControls: () => dockControls,
    scrollBottomPadding: () => scrollBottomPadding,
    scrollMaxHeight: () => scrollMaxHeight,
    scrollMinHeight: () => scrollMinHeight,
  });
  // svelte-ignore state_referenced_locally
  const pagination = new EntityGridPaginationController({
    initialPageSize: persistedPrefs?.pageSize ?? normalizeEntityGridPageSize(initialPageSize),
    pageSizeOptions: () => pageSizeOptions,
    sourceCount: () => cards.length,
    visibleCount: () => visibleCards.length,
    hasMore: () => hasMore,
    loading: () => loading,
    loadingMore: () => loadingMore,
    loadMoreError: () => loadMoreError,
    remoteTotalCount: () => remoteTotalCount,
    showPagination: () => showPagination,
    onLoadMore: () => onLoadMore,
    onPageSizeChange: () => onPageSizeChange,
    onNavigate: viewport.scrollPageToTop,
  });
  const selectedCount = $derived(selectedIds.length);
  const selectedCards = $derived(
    selectedCount > 0
      ? effectiveCards.filter((c) => selectedIds.includes(c.entity.id))
      : [],
  );
  const allSelectedNsfw = $derived(
    selectedCards.length > 0 && selectedCards.every((c) => isNsfw(c.entity.capabilities)),
  );
  // "Remove wanted" is offered only when the whole selection is wanted placeholders — mixing in real
  // on-disk items would make the action ambiguous (the server skips them anyway).
  const allSelectedWanted = $derived(
    selectedCards.length > 0 && selectedCards.every((c) => isWanted(c.entity.capabilities)),
  );
  // Members of the current selection that can live in a collection, mapped to the
  // collection item reference shape. Kinds the backend rejects (people, studios,
  // tags) are dropped so the Add to Collection menu only offers eligible items.
  const collectionItems = $derived(
    selectedCards
      .filter((c) => isCollectionEntityType(c.entity.kind))
      .map((c) => ({ entityType: c.entity.kind as CollectionEntityType, entityId: c.entity.id })),
  );
  const request = $derived(entityGridRequestFromState(gridState, filterOptions));
  const canClearFilters = $derived(Boolean(
    activeKind !== ENTITY_GRID_ALL_KINDS || filterIds.length > 0 || !includeNsfw || query,
  ));
  const pagedCards = $derived(pagination.page(visibleCards));

  interface EntityGridSnapshot {
    query: string;
    activeKind: string;
    filterIds: string[];
    includeNsfw: boolean;
    sortBy: EntityGridSort;
    sortDir: EntityGridSortDir;
    randomSeed?: number;
    viewMode: EntityGridViewMode;
    mediaWall?: boolean;
    selectedIds: string[];
    scale: number;
    pageIndex: number;
    pageSize: number;
  }

  const pageSnapshots = usePageSnapshots();

  onMount(() => {
    if (mediaWall && viewMode === "list") viewMode = "grid";
    pagination.notifyPageSize();
    const key = entityGridPresetStorageKey(prefsKey);
    if (key) presets = createFilterPresets(key).load();

    if (!prefsKey) return;
    return pageSnapshots.registerSurface<EntityGridSnapshot>(`entity-grid:${prefsKey}`, {
      capture: () => ({
        query,
        activeKind,
        filterIds: [...filterIds],
        includeNsfw,
        sortBy,
        sortDir,
        randomSeed,
        viewMode,
        mediaWall,
        selectedIds: [...selectedIds],
        scale,
        pageIndex: pagination.currentPageIndex,
        pageSize: pagination.pageSize,
      }),
      restore: (snapshot) => {
        query = snapshot.query;
        activeKind = snapshot.activeKind;
        filterIds = snapshot.filterIds;
        includeNsfw = snapshot.includeNsfw;
        sortBy = snapshot.sortBy;
        sortDir = snapshot.sortDir;
        randomSeed = snapshot.randomSeed ?? randomSeed;
        viewMode = snapshot.viewMode;
        mediaWall = snapshot.mediaWall ?? initialMediaWall;
        if (mediaWall && viewMode === "list") viewMode = "grid";
        selectedIds = snapshot.selectedIds;
        scale = snapshot.scale;
        pagination.restore(
          snapshot.pageIndex ?? 0,
          snapshot.pageSize ?? pagination.pageSize,
        );
        onSelectionChange?.(selectedIds);
      },
    });
  });

  onMount(viewport.connect);

  $effect(() => {
    onRequestChange?.(request);
  });

  // Persist the full view state for this grid whenever a tracked control
  // changes. Reading every field here registers them as dependencies so any
  // change re-runs the effect. Only non-default state is stored; returning a
  // grid to its defaults clears the entry so stale view state never lingers.
  $effect(() => {
    const snapshot: EntityGridPrefs = {
      query,
      activeKind,
      filterIds: [...filterIds],
      includeNsfw,
      sortBy,
      sortDir,
      viewMode,
      mediaWall,
      scale,
      pageSize: pagination.pageSize,
      activePresetId,
      barsCollapsed,
    };
    if (!prefsStore) return;
    if (prefsStore.isDefault(snapshot)) prefsStore.clear();
    else prefsStore.save(snapshot);
  });

  $effect(() => {
    onRenderedCountChange?.(pagedCards.length);
  });

  $effect(() => {
    const visibleIds = new Set(visibleCards.map((card) => card.entity.id));
    const nextSelected = selectedIds.filter((id) => visibleIds.has(id));
    if (nextSelected.length !== selectedIds.length) {
      selectedIds = nextSelected;
      onSelectionChange?.(selectedIds);
    }
  });

  function persistScale(next: number) {
    scale = clampScale(next);
  }

  function setActiveKind(kind: string) {
    activeKind = kind;
    activePresetId = null;
    pagination.resetPage();
    selectedIds = [];
    onSelectionChange?.(selectedIds);
  }

  function setFilterIds(ids: string[]) {
    filterIds = ids;
    activePresetId = null;
    pagination.resetPage();
  }

  function setIncludeNsfw(value: boolean) {
    includeNsfw = value;
    activePresetId = null;
    pagination.resetPage();
    selectedIds = [];
    onSelectionChange?.(selectedIds);
  }

  function setQuery(value: string) {
    query = value;
    activePresetId = null;
    pagination.resetPage();
  }

  /** Generates a fresh, non-zero seed for the random shuffle. */
  function nextRandomSeed(): number {
    return Math.floor(Math.random() * 2_000_000_000) + 1;
  }

  function setSortBy(value: EntityGridSort) {
    // Re-selecting Random reshuffles; selecting it for the first time seeds it.
    if (value === "random") {
      randomSeed = nextRandomSeed();
      pagination.resetPage();
    }
    sortBy = value;
    activePresetId = null;
  }

  /** Reshuffles the current random ordering with a new seed. */
  function reshuffle() {
    randomSeed = nextRandomSeed();
    pagination.resetPage();
  }

  function setSortDir(value: EntityGridSortDir) {
    sortDir = value;
    activePresetId = null;
  }

  function setViewMode(value: EntityGridViewMode) {
    viewMode = value;
    // Media wall applies to the grid and the feed but not the row-based list.
    if (value === "list") {
      mediaWall = false;
    }
  }

  function setMediaWall(value: boolean) {
    mediaWall = value;
    // The list layout has no media-wall variant, so enabling it there falls back to the grid.
    if (value && viewMode === "list") viewMode = "grid";
  }

  function savePresets(next: FilterPreset[]) {
    presets = next;
    const key = entityGridPresetStorageKey(prefsKey);
    if (key) createFilterPresets(key).save(next);
  }

  function applyPreset(preset: FilterPreset) {
    const next = readEntityGridPreset({
      preset,
      filterOptions,
      fallbackSortBy: initialSortBy,
    });
    filterIds = next.filterIds;
    sortBy = next.sortBy;
    if (sortBy === "random") randomSeed = nextRandomSeed();
    sortDir = next.sortDir;
    activePresetId = preset.id;
    pagination.resetPage();
  }

  function savePreset(name: string) {
    const id = createEntityGridPresetId();
    const next = [
      createEntityGridPreset({ id, name, filterIds, filterOptions, sortBy, sortDir }),
      ...presets,
    ].slice(0, 20);
    activePresetId = id;
    savePresets(next);
  }

  function overwritePreset(id: string) {
    const existing = presets.find((preset) => preset.id === id);
    if (!existing) return;
    const next = createEntityGridPreset({
      id,
      name: existing.name,
      filterIds,
      filterOptions,
      sortBy,
      sortDir,
    });
    savePresets(presets.map((preset) => (preset.id === id ? next : preset)));
    activePresetId = id;
  }

  function deletePreset(id: string) {
    savePresets(presets.filter((preset) => preset.id !== id));
    if (activePresetId === id) activePresetId = null;
  }

  function clearFilters() {
    activeKind = ENTITY_GRID_ALL_KINDS;
    activePresetId = null;
    filterIds = [];
    includeNsfw = true;
    query = "";
    pagination.resetPage();
  }

  function updateSelection(id: string, selected: boolean) {
    selectedIds = selected
      ? Array.from(new Set([...selectedIds, id]))
      : selectedIds.filter((selectedId) => selectedId !== id);
    onSelectionChange?.(selectedIds);
  }

  function setSelectionActive(active: boolean) {
    selectionActive = active;
    if (!active && selectedIds.length > 0) {
      selectedIds = [];
      onSelectionChange?.(selectedIds);
    }
  }

  function selectAllVisible() {
    selectedIds = visibleCards.map((c) => c.entity.id);
    onSelectionChange?.(selectedIds);
  }

  function clearSelection() {
    selectedIds = [];
    onSelectionChange?.(selectedIds);
  }

  /**
   * Removes the selected wanted placeholders: the server deletes each (tearing down in-flight
   * downloads) and blacklists it from container discovery so a followed author/artist sweep never
   * brings it back — requesting the exact item again later clears its blacklist entry. Each Entity is sent
   * separately so a retained/failed durable teardown remains visible and selected for an explicit retry.
   */
  async function removeWantedSelection() {
    if (selectedCards.length === 0) return;
    removeWantedErrors = [];
    const targets = selectedCards.map((card) => card.entity.id);
    const confirmed: string[] = [];
    const failures: string[] = [];
    for (const target of targets) {
      try {
        const result = await removeWantedEntities([target]);
        if (Number(result.removed) === 1) {
          confirmed.push(target);
          continue;
        }

        const messages = result.failures
          .filter((failure) => failure.entityId === target)
          .map((failure) => failure.message);
        failures.push(...(messages.length > 0
          ? messages
          : ["The wanted item could not be removed. Refresh and try again."]));
      } catch (error) {
        // This target stays selected and visible; continue so one outage does not strand other removals.
        failures.push(error instanceof Error ? error.message : "The wanted item could not be removed.");
      }
    }

    removeWantedErrors = Array.from(new Set(failures));

    if (confirmed.length > 0) {
      const confirmedIds = new Set(confirmed);
      removedIds = new Set([...removedIds, ...confirmed]);
      selectedIds = selectedIds.filter((id) => !confirmedIds.has(id));
      onSelectionChange?.(selectedIds);
    }
  }

  function toggleNsfwFlag(markNsfw: boolean) {
    if (selectedCards.length === 0) return;
    const targets = [...selectedCards];
    for (const card of targets) {
      capabilityOverrides.set(
        card.entity.id,
        withFlagCapability(card.entity.capabilities, "isNsfw", markNsfw),
      );
    }
    for (const card of targets) {
      void updateEntityFlags(card.entity.id, { isNsfw: markNsfw });
    }
  }

</script>

<section
  bind:this={viewport.sectionEl}
  class="entity-grid"
  class:is-static={!dockControls}
  style:--col-count={scale}
  style:--entity-grid-fill-height={viewport.measuredFillHeight ?? undefined}
>
  <EntityGridToolbar
    activeFilterIds={filterIds}
    {activePresetId}
    {allSelectedNsfw}
    {allSelectedWanted}
    onRemoveWanted={removeWantedSelection}
    {barsCollapsed}
    {bulkActions}
    collectionItems={bulkLibraryActions ? collectionItems : []}
    showNsfwAction={bulkLibraryActions}
    {canClearFilters}
    {enableFeedView}
    {drawerOpen}
    {entityKind}
    {filterOptions}
    {maxScale}
    {mediaWall}
    {minScale}
    onActiveFilterIdsChange={setFilterIds}
    onApplyPreset={applyPreset}
    onBarsCollapsedChange={(collapsed) => (barsCollapsed = collapsed)}
    onClearFilters={clearFilters}
    onClearSelection={clearSelection}
    onDeletePreset={deletePreset}
    onDrawerOpenChange={(open) => (drawerOpen = open)}
    onMediaWallChange={setMediaWall}
    onOverwritePreset={overwritePreset}
    onQueryChange={setQuery}
    onSelectAllVisible={selectAllVisible}
    onSelectionActiveChange={setSelectionActive}
    onSavePreset={savePreset}
    onScaleChange={persistScale}
    onSortByChange={setSortBy}
    onSortDirChange={setSortDir}
    onToggleNsfwFlag={toggleNsfwFlag}
    onReshuffle={reshuffle}
    onViewModeChange={setViewMode}
    {presets}
    {query}
    {scale}
    {selectable}
    {selectedCount}
    {selectedIds}
    {selectionActive}
    {sortBy}
    {sortDir}
    {viewMode}
  />

  {#if removeWantedErrors.length > 0}
    <div class="remove-wanted-error" role="alert">
      {#each removeWantedErrors as message (message)}
        <p>{message}</p>
      {/each}
    </div>
  {/if}

  {#if drawerOpen}
    <EntityGridFilterDrawer
      activeFilterIds={filterIds}
      {filterOptions}
      {entityKind}
      {lockBookFilters}
      onActiveFilterIdsChange={setFilterIds}
    />
  {/if}

  <EntityGridTabs
    {activeKind}
    onActiveKindChange={setActiveKind}
    {tabs}
    totalCount={cards.length}
  />

  <div
    bind:this={viewport.viewportEl}
    class={["grid-viewport", viewport.containsScroll && "is-contained"]}
    style:--entity-grid-scroll-max-height={viewport.effectiveScrollMaxHeight ?? undefined}
    onwheel={viewport.markScrolling}
  >
    <EntityGridContent
      {cardLinks}
      cards={pagedCards}
      {emptyMessage}
      {emptyTitle}
      onResetFilters={canClearFilters ? clearFilters : undefined}
      hasVisibleCards={visibleCards.length > 0}
      hoverPreviewSuppressed={viewport.areHoverPreviewsSuppressed}
      {loading}
      {mediaWall}
      {onCardActivate}
      onCardSelectedChange={updateSelection}
      {selectable}
      {selectedIds}
      {selectionActive}
      {viewMode}
    />
  </div>

  {#if pagination.shouldRender}
    <EntityGridPagination
      canPageBack={pagination.canPageBack}
      canPageForward={pagination.canPageForward}
      canSeekToEnd={pagination.canSeekToEnd}
      currentPageIndex={pagination.currentPageIndex}
      effectiveTotal={pagination.effectiveTotal}
      {loadMoreError}
      {loadingMore}
      normalizedPageSizeOptions={pagination.normalizedPageSizeOptions}
      onFirstPage={() => pagination.setPageIndex(0)}
      onLastPage={pagination.goToLastPage}
      {onLoadMore}
      onNextPage={pagination.goToNextPage}
      onPageSizeChange={pagination.setPageSize}
      onPreviousPage={() => pagination.setPageIndex(pagination.currentPageIndex - 1)}
      pageCount={pagination.pageCount}
      pageEnd={pagination.pageEnd}
      pageSize={pagination.pageSize}
      pageStart={pagination.pageStart}
      pendingAdvanceAfterLoad={pagination.pendingAdvanceAfterLoad}
      readoutPlaceholderWidth={pagination.readoutPlaceholderWidth}
      totalIsExact={pagination.totalIsExact}
    />
  {/if}
</section>

<style>
  /*
   * Use a flex column so the toolbar, tabs, cards, and pagination remain in
   * normal document flow. The top toolbar can stay sticky independently, while
   * the pagination controls live at the natural end of the list where paging
   * decisions are made.
   */
  .entity-grid {
    display: flex;
    flex-direction: column;
    min-height: var(--entity-grid-fill-height, 0);
    min-width: 0;
  }

  .entity-grid.is-static {
    min-height: 0;
  }

  /*
   * Use explicit sibling margins instead of flex `gap` so we can zero out the
   * space directly after the sticky toolbar. The thumbnail viewport owns its
   * own block padding, so cards get visible breathing room above and below
   * without creating a transparent strip inside the sticky toolbar shell.
   */
  :global(.entity-grid > * + *) {
    margin-top: 0.85rem;
  }

  :global(.entity-grid > :first-child + *) {
    margin-top: 0;
  }

  .entity-grid > :global(.pagination-shell),
  .entity-grid.is-static > :global(.pagination-shell) {
    margin-top: 0.85rem;
  }

  .remove-wanted-error {
    display: grid;
    gap: 0.25rem;
    border: 1px solid color-mix(in srgb, var(--color-error) 40%, transparent);
    border-radius: var(--radius-xs);
    background: color-mix(in srgb, var(--color-error) 10%, transparent);
    padding: 0.65rem 0.8rem;
    color: var(--color-error-text);
    font-size: 0.78rem;
  }

  .remove-wanted-error p {
    margin: 0;
  }

  .grid-viewport {
    display: grid;
    gap: 0.85rem;
    box-sizing: border-box;
    min-height: 0;
    padding-block: 0.85rem;
  }

  /*
   * The viewport no longer establishes its own scrolling container. Cards
   * flow naturally in the layout's main scroll, and the sticky toolbar floats
   * over them. Without an inner overflow container, content can't be clipped
   * behind the sticky toolbar - the cards just slide under it.
   */
  .grid-viewport.is-contained {
    min-height: 0;
  }

  .entity-grid.is-static :global(.toolbar-shell) {
    position: relative;
    top: auto;
    padding-top: 0;
  }

  .entity-grid.is-static :global(.toolbar-shell::before) {
    display: none;
  }

</style>
