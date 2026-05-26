<script lang="ts">
  import { browser } from "$app/environment";
  import {
    ChevronDown,
    ChevronLeft,
    ChevronRight,
    ChevronsLeft,
    ChevronsRight,
    EllipsisVertical,
    Flame,
    LoaderCircle,
    SearchX,
    Check,
    CheckCheck,
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { onMount } from "svelte";
  import { isNsfw, withFlagCapability } from "$lib/api/capabilities";
  import type { EntityCapability } from "$lib/api/generated/model";
  import { updateEntityFlags } from "$lib/api/prismedia";
  import { createFilterPresets, type FilterPreset } from "$lib/filter-presets";
  import { usePageSnapshots } from "$lib/stores/page-snapshots.svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import {
    ENTITY_GRID_ALL_KINDS,
    applyEntityGridState,
    buildCapabilityFilterOptions,
    buildEntityKindTabs,
    entityGridRequestFromState,
    entityGridFilterFromId,
    type EntityGridRequest,
    type EntityGridSort,
    type EntityGridSortDir,
    type EntityGridViewMode,
    type EntityGridBulkAction,
  } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityGridFilterDrawer from "./EntityGridFilterDrawer.svelte";
  import EntityGridTabs from "./EntityGridTabs.svelte";
  import EntityGridToolbar from "./EntityGridToolbar.svelte";
  import { computeContainedScrollHeight } from "./entity-grid-viewport.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import type { NsfwMode } from "$lib/nsfw/cookie";

  const DEFAULT_PAGE_SIZE = 250;
  const DEFAULT_PAGE_SIZE_OPTIONS = [100, 250, 500, 1000];
  const DEFAULT_SCALE = 5;
  const MOBILE_THUMBNAIL_QUERY = "(max-width: 639.98px)";

  interface Props {
    bulkActions?: EntityGridBulkAction[];
    cards: EntityThumbnailCard[];
    emptyMessage?: string;
    emptyTitle?: string;
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
  }

  let {
    bulkActions = [],
    cards,
    emptyMessage = "Try adjusting your search or filters.",
    emptyTitle = "Nothing present",
    hasMore = false,
    initialPageSize = DEFAULT_PAGE_SIZE,
    initialMediaWall = false,
    initialSortBy = "title",
    initialSortDir = "asc",
    dockControls = true,
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
  }: Props = $props();

  function storageKey(): string | null {
    return prefsKey ? `prismedia:entity-grid:${prefsKey}` : null;
  }

  function presetStorageKey(): string | null {
    return prefsKey ? `prismedia:entity-grid-presets:${prefsKey}` : null;
  }

  function pageSizeStorageKey(): string | null {
    return prefsKey ? `prismedia:entity-grid-page-size:${prefsKey}` : null;
  }

  function mediaWallStorageKey(): string | null {
    return prefsKey ? `prismedia:entity-grid-media-wall:${prefsKey}` : null;
  }

  function isMobileThumbnailViewport(): boolean {
    return browser &&
      typeof window.matchMedia === "function" &&
      window.matchMedia(MOBILE_THUMBNAIL_QUERY).matches;
  }

  function defaultScale(): number {
    return isMobileThumbnailViewport() ? minScale : DEFAULT_SCALE;
  }

  function loadScale(): number {
    const fallbackScale = defaultScale();
    if (!browser) return fallbackScale;
    const key = storageKey();
    if (!key) return fallbackScale;
    const raw = window.localStorage.getItem(key);
    if (raw == null) return fallbackScale;
    const parsed = Number(raw);
    return Number.isFinite(parsed) ? Math.min(maxScale, Math.max(minScale, parsed)) : fallbackScale;
  }

  function normalizePageSize(value: number): number {
    const numeric = Math.floor(value);
    return Number.isFinite(numeric) && numeric > 0 ? numeric : DEFAULT_PAGE_SIZE;
  }

  function loadPageSize(): number {
    const fallback = normalizePageSize(initialPageSize);
    if (!browser) return fallback;
    const key = pageSizeStorageKey();
    if (!key) return fallback;
    const raw = window.localStorage.getItem(key);
    if (raw == null) return fallback;
    return normalizePageSize(Number(raw));
  }

  function loadMediaWall(): boolean {
    if (!browser) return initialMediaWall;
    const key = mediaWallStorageKey();
    if (!key) return initialMediaWall;
    const raw = window.localStorage.getItem(key);
    if (raw === "true") return true;
    if (raw === "false") return false;
    return initialMediaWall;
  }

  let actionsMenuOpen = $state(false);
  let capabilityOverrides = $state(new Map<string, EntityCapability[]>());
  let activeKind = $state(ENTITY_GRID_ALL_KINDS);
  let activePresetId = $state<string | null>(null);
  let drawerOpen = $state(false);
  let filterIds = $state<string[]>([]);
  let includeNsfw = $state(true);
  let presets = $state<FilterPreset[]>([]);
  let query = $state("");
  let pageIndex = $state(0);
  let pageSize = $state(DEFAULT_PAGE_SIZE);
  let pageSizeOpen = $state(false);
  let pendingAdvanceAfterLoad = $state(false);
  let scale = $state(DEFAULT_SCALE);
  // svelte-ignore state_referenced_locally
  let mediaWall = $state(initialMediaWall);
  let selectedIds = $state<string[]>([]);
  let viewportEl: HTMLDivElement | undefined = $state();
  let sectionEl: HTMLElement | undefined = $state();
  let measuredScrollMaxHeight = $state<string | null>(null);
  let measuredFillHeight = $state<string | null>(null);
  let scrolling = $state(false);
  let scrollEndTimer: number | null = null;
  // svelte-ignore state_referenced_locally
  let sortBy = $state<EntityGridSort>(initialSortBy);
  // svelte-ignore state_referenced_locally
  let sortDir = $state<EntityGridSortDir>(initialSortDir);
  let viewMode = $state<EntityGridViewMode>("grid");
  const nsfw = useNsfw();
  const effectiveNsfwMode = $derived(nsfwMode ?? nsfw.mode);

  const gridState = $derived({
    activeKind,
    filterIds,
    includeNsfw: effectiveNsfwMode === "show" && includeNsfw,
    query,
    sortBy,
    sortDir,
  });
  const effectiveCards = $derived.by(() => {
    if (capabilityOverrides.size === 0) return cards;
    return cards.map((c) => {
      const overridden = capabilityOverrides.get(c.entity.id);
      if (!overridden) return c;
      return { ...c, entity: { ...c.entity, capabilities: overridden } };
    });
  });
  const tabs = $derived(buildEntityKindTabs(effectiveCards, { includeNsfw: gridState.includeNsfw }));
  const filterOptions = $derived(buildCapabilityFilterOptions(effectiveCards));
  const visibleCards = $derived(applyEntityGridState(effectiveCards, gridState, filterOptions));
  const selectedCount = $derived(selectedIds.length);
  const selectedCards = $derived(
    selectedCount > 0
      ? effectiveCards.filter((c) => selectedIds.includes(c.entity.id))
      : [],
  );
  const allSelectedNsfw = $derived(
    selectedCards.length > 0 && selectedCards.every((c) => isNsfw(c.entity.capabilities)),
  );
  const request = $derived(entityGridRequestFromState(gridState, filterOptions));
  const effectiveScrollMaxHeight = $derived(
    dockControls ? scrollMaxHeight === undefined ? measuredScrollMaxHeight : scrollMaxHeight : null,
  );
  const containsScroll = $derived(dockControls && scrollMaxHeight !== null);
  const normalizedPageSizeOptions = $derived(
    Array.from(new Set([...pageSizeOptions, pageSize].map(normalizePageSize))).sort((a, b) => a - b),
  );
  const paginationThreshold = $derived(normalizedPageSizeOptions[0] ?? normalizePageSize(initialPageSize));
  /**
   * Server total reflects what's matched by remote filters (kind, hideNsfw). When
   * the grid additionally applies a local search/capability filter we fall back to
   * the locally visible count so the pagination strip reads honestly. When no
   * remote count is supplied (e.g. test harness, non-paged grids), we use the
   * loaded card count plus a "+1" sentinel if more pages remain.
   */
  const isLocallyFiltered = $derived(visibleCards.length !== cards.length);
  const knownRemoteTotal = $derived(remoteTotalCount != null && remoteTotalCount >= 0 ? remoteTotalCount : null);
  const effectiveTotal = $derived(
    isLocallyFiltered
      ? visibleCards.length
      : knownRemoteTotal != null
        ? knownRemoteTotal
        : cards.length + (hasMore ? 1 : 0),
  );
  /**
   * True when the readout total is exact (server-confirmed full count of items in
   * scope). When false we render the count with a trailing `+` so the user
   * understands more results may exist beyond what's been loaded.
   */
  const totalIsExact = $derived(isLocallyFiltered ? !hasMore : knownRemoteTotal != null);
  const pageCount = $derived(Math.max(1, Math.ceil(effectiveTotal / pageSize)));
  const currentPageIndex = $derived(Math.min(pageIndex, pageCount - 1));
  const pageStart = $derived(effectiveTotal === 0 ? 0 : currentPageIndex * pageSize);
  const pageEnd = $derived(Math.min(effectiveTotal, pageStart + pageSize));
  const pagedCards = $derived(visibleCards.slice(pageStart, Math.min(visibleCards.length, pageStart + pageSize)));
  const hoverPreviewsEnabled = $derived(!scrolling);
  const canPageBack = $derived(currentPageIndex > 0);
  const canPageForward = $derived(currentPageIndex < pageCount - 1 || Boolean(hasMore && onLoadMore));
  const canSeekToEnd = $derived(currentPageIndex < pageCount - 1);
  const shouldRenderPagination = $derived(
    showPagination &&
      !loading &&
      visibleCards.length > 0 &&
      (effectiveTotal > paginationThreshold ||
        pageCount > 1 ||
        currentPageIndex > 0 ||
        Boolean(hasMore) ||
        Boolean(loadMoreError)),
  );
  const showPageSizeMenu = $derived(pageSizeOpen && shouldRenderPagination);
  /** Widest possible string for the readout, used to reserve a stable layout slot. */
  const readoutPlaceholderWidth = $derived(
    Math.max(String(effectiveTotal).length, String(pageStart + 1).length, String(pageEnd).length) * 2 + 4,
  );

  interface EntityGridSnapshot {
    query: string;
    activeKind: string;
    filterIds: string[];
    includeNsfw: boolean;
    sortBy: EntityGridSort;
    sortDir: EntityGridSortDir;
    viewMode: EntityGridViewMode;
    mediaWall?: boolean;
    selectedIds: string[];
    scale: number;
    pageIndex: number;
    pageSize: number;
  }

  const pageSnapshots = usePageSnapshots();

  onMount(() => {
    scale = loadScale();
    pageSize = loadPageSize();
    mediaWall = loadMediaWall();
    if (mediaWall) viewMode = "grid";
    onPageSizeChange?.(pageSize);
    const key = presetStorageKey();
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
        viewMode,
        mediaWall,
        selectedIds: [...selectedIds],
        scale,
        pageIndex: currentPageIndex,
        pageSize,
      }),
      restore: (snapshot) => {
        query = snapshot.query;
        activeKind = snapshot.activeKind;
        filterIds = snapshot.filterIds;
        includeNsfw = snapshot.includeNsfw;
        sortBy = snapshot.sortBy;
        sortDir = snapshot.sortDir;
        viewMode = snapshot.viewMode;
        mediaWall = snapshot.mediaWall ?? loadMediaWall();
        if (mediaWall) viewMode = "grid";
        selectedIds = snapshot.selectedIds;
        scale = snapshot.scale;
        pageSize = normalizePageSize(snapshot.pageSize ?? pageSize);
        pageIndex = Math.max(0, snapshot.pageIndex ?? 0);
        onPageSizeChange?.(pageSize);
        onSelectionChange?.(selectedIds);
      },
    });
  });

  onMount(() => {
    let raf: number | null = null;
    let observer: ResizeObserver | null = null;

    function findScrollAncestorBottom(el: Element): number {
      // Walk up to the nearest scrolling ancestor (e.g. the layout's <main>)
      // and clip the available height there. window.innerHeight overshoots on
      // mobile because the layout reserves a band at the bottom for the fixed
      // MobileNav — anchoring against the scrolling container keeps the
      // pagination strip above that band instead of behind it.
      let current: Element | null = el.parentElement;
      while (current && current !== document.body && current !== document.documentElement) {
        const cs = getComputedStyle(current);
        const overflows = cs.overflowY === "auto" || cs.overflowY === "scroll";
        if (overflows) {
          return current.getBoundingClientRect().bottom;
        }
        current = current.parentElement;
      }
      return window.innerHeight;
    }

    function measureViewport() {
      if (!dockControls || !viewportEl || scrollMaxHeight !== undefined) {
        measuredScrollMaxHeight = null;
        measuredFillHeight = null;
        return;
      }

      const containerBottom = findScrollAncestorBottom(viewportEl);

      measuredScrollMaxHeight = computeContainedScrollHeight({
        bottomPadding: scrollBottomPadding,
        minHeight: scrollMinHeight,
        top: viewportEl.getBoundingClientRect().top,
        viewportHeight: containerBottom,
      });

      /*
       * Total fill height for the entity-grid flex column. We anchor against
       * the section's top edge (rather than the inner viewport's) so the
       * toolbar/tabs above the viewport are included in the fill area without
       * inflating the card viewport with empty scrollable space.
       */
      if (sectionEl) {
        const sectionTop = sectionEl.getBoundingClientRect().top;
        const fill = Math.max(0, Math.floor(containerBottom - sectionTop - scrollBottomPadding));
        measuredFillHeight = `${fill}px`;
      } else {
        measuredFillHeight = null;
      }
    }

    function scheduleMeasure() {
      if (raf !== null) return;
      raf = requestAnimationFrame(() => {
        raf = null;
        measureViewport();
      });
    }

    observer = new ResizeObserver(scheduleMeasure);
    if (viewportEl) observer.observe(viewportEl);
    if (sectionEl) observer.observe(sectionEl);
    window.addEventListener("resize", scheduleMeasure, { passive: true });
    queueMicrotask(measureViewport);

    return () => {
      observer?.disconnect();
      window.removeEventListener("resize", scheduleMeasure);
      if (raf !== null) cancelAnimationFrame(raf);
    };
  });

  function clearScrollEndTimer() {
    if (scrollEndTimer === null) return;
    window.clearTimeout(scrollEndTimer);
    scrollEndTimer = null;
  }

  function markScrolling() {
    scrolling = true;
    clearScrollEndTimer();
    scrollEndTimer = window.setTimeout(() => {
      scrollEndTimer = null;
      scrolling = false;
    }, 180);
  }

  onMount(() => {
    window.addEventListener("scroll", markScrolling, { capture: true, passive: true });

    return () => {
      window.removeEventListener("scroll", markScrolling, { capture: true });
      clearScrollEndTimer();
    };
  });

  $effect(() => {
    onRequestChange?.(request);
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
    scale = Math.min(maxScale, Math.max(minScale, next));
    const key = storageKey();
    if (browser && key) window.localStorage.setItem(key, String(scale));
  }

  function persistMediaWall(next: boolean) {
    const key = mediaWallStorageKey();
    if (browser && key) window.localStorage.setItem(key, String(next));
  }

  function setActiveKind(kind: string) {
    activeKind = kind;
    activePresetId = null;
    pageIndex = 0;
    selectedIds = [];
    onSelectionChange?.(selectedIds);
  }

  function setFilterIds(ids: string[]) {
    filterIds = ids;
    activePresetId = null;
    pageIndex = 0;
  }

  function setIncludeNsfw(value: boolean) {
    includeNsfw = value;
    activePresetId = null;
    pageIndex = 0;
    selectedIds = [];
    onSelectionChange?.(selectedIds);
  }

  function setQuery(value: string) {
    query = value;
    activePresetId = null;
    pageIndex = 0;
  }

  function setSortBy(value: EntityGridSort) {
    sortBy = value;
    activePresetId = null;
  }

  function setSortDir(value: EntityGridSortDir) {
    sortDir = value;
    activePresetId = null;
  }

  function setViewMode(value: EntityGridViewMode) {
    viewMode = value;
    if (value === "list") {
      mediaWall = false;
      persistMediaWall(mediaWall);
    }
  }

  function setMediaWall(value: boolean) {
    mediaWall = value;
    if (value) viewMode = "grid";
    persistMediaWall(mediaWall);
  }

  function savePresets(next: FilterPreset[]) {
    presets = next;
    const key = presetStorageKey();
    if (key) createFilterPresets(key).save(next);
  }

  function filterToPresetEntry(id: string) {
    const option = entityGridFilterFromId(id, filterOptions);
    return {
      label: option?.label ?? id,
      type: option?.capabilityKind ?? "capability",
      value: id,
    };
  }

  function currentPresetShape(id: string, name: string): FilterPreset {
    return {
      id,
      name,
      filters: filterIds.map(filterToPresetEntry),
      sortBy,
      sortDir,
    };
  }

  function applyPreset(preset: FilterPreset) {
    filterIds = preset.filters
      .map((filter) => filter.value)
      .filter((id) => Boolean(entityGridFilterFromId(id, filterOptions)));
    sortBy = preset.sortBy === "kind" || preset.sortBy === "rating" || preset.sortBy === "position" ? preset.sortBy : initialSortBy;
    sortDir = preset.sortDir;
    activePresetId = preset.id;
    pageIndex = 0;
  }

  function savePreset(name: string) {
    const id = `entity-grid-preset-${Date.now().toString(36)}`;
    const next = [currentPresetShape(id, name), ...presets].slice(0, 20);
    activePresetId = id;
    savePresets(next);
  }

  function overwritePreset(id: string) {
    const existing = presets.find((preset) => preset.id === id);
    if (!existing) return;
    savePresets(presets.map((preset) => (preset.id === id ? currentPresetShape(id, existing.name) : preset)));
    activePresetId = id;
  }

  function deletePreset(id: string) {
    savePresets(presets.filter((preset) => preset.id !== id));
    if (activePresetId === id) activePresetId = null;
  }

  function clearFiltersAndSort() {
    activeKind = ENTITY_GRID_ALL_KINDS;
    activePresetId = null;
    actionsMenuOpen = false;
    filterIds = [];
    includeNsfw = true;
    query = "";
    selectedIds = [];
    sortBy = initialSortBy;
    sortDir = initialSortDir;
    viewMode = "grid";
    mediaWall = initialMediaWall;
    persistMediaWall(mediaWall);
    pageIndex = 0;
    onSelectionChange?.(selectedIds);
  }

  function updateSelection(id: string, selected: boolean) {
    selectedIds = selected
      ? Array.from(new Set([...selectedIds, id]))
      : selectedIds.filter((selectedId) => selectedId !== id);
    onSelectionChange?.(selectedIds);
  }

  function toggleNsfwFlag(markNsfw: boolean) {
    if (selectedCards.length === 0) return;
    const targets = [...selectedCards];
    const next = new Map(capabilityOverrides);
    for (const card of targets) {
      next.set(card.entity.id, withFlagCapability(card.entity.capabilities, "isNsfw", markNsfw));
    }
    capabilityOverrides = next;
    for (const card of targets) {
      void updateEntityFlags(card.entity.id, { isNsfw: markNsfw });
    }
  }

  function scrollPageToTop() {
    viewportEl?.scrollTo({ top: 0 });
  }

  function setPageIndex(next: number) {
    pageIndex = Math.max(0, Math.min(pageCount - 1, next));
    queueMicrotask(scrollPageToTop);
  }

  function setPageSize(value: number) {
    pageSize = normalizePageSize(value);
    pageIndex = 0;
    const key = pageSizeStorageKey();
    if (browser && key) window.localStorage.setItem(key, String(pageSize));
    onPageSizeChange?.(pageSize);
    queueMicrotask(scrollPageToTop);
  }

  /** Load enough remote pages to make the target page index renderable. */
  async function ensurePageLoaded(targetPage: number) {
    if (!hasMore || !onLoadMore) return;
    const targetStart = targetPage * pageSize;
    while (visibleCards.length <= targetStart && hasMore) {
      const previousCount = visibleCards.length;
      await onLoadMore();
      if (visibleCards.length <= previousCount) break;
    }
  }

  async function goToNextPage() {
    if (currentPageIndex < pageCount - 1) {
      // If the next page exists locally, just jump. If it doesn't (we know the
      // total but haven't buffered enough rows yet), buffer enough cursor pages
      // to render it before advancing.
      const targetPage = currentPageIndex + 1;
      if (visibleCards.length > targetPage * pageSize || !hasMore) {
        setPageIndex(targetPage);
        return;
      }
      pendingAdvanceAfterLoad = true;
      try {
        await ensurePageLoaded(targetPage);
        setPageIndex(targetPage);
      } finally {
        pendingAdvanceAfterLoad = false;
      }
      return;
    }

    if (!hasMore || !onLoadMore || loadingMore) return;
    const targetPage = currentPageIndex + 1;
    pendingAdvanceAfterLoad = true;
    try {
      await ensurePageLoaded(targetPage);
      setPageIndex(targetPage);
    } finally {
      pendingAdvanceAfterLoad = false;
    }
  }

  async function goToLastPage() {
    const lastPage = pageCount - 1;
    if (lastPage <= currentPageIndex) return;
    // If we already have the data, just jump.
    if (visibleCards.length > lastPage * pageSize || !hasMore) {
      setPageIndex(lastPage);
      return;
    }
    pendingAdvanceAfterLoad = true;
    try {
      await ensurePageLoaded(lastPage);
      setPageIndex(Math.min(lastPage, pageCount - 1));
    } finally {
      pendingAdvanceAfterLoad = false;
    }
  }
</script>

<section
  bind:this={sectionEl}
  class="entity-grid"
  class:is-static={!dockControls}
  style:--col-count={scale}
  style:--entity-grid-fill-height={measuredFillHeight ?? undefined}
>
  <EntityGridToolbar
    activeFilterIds={filterIds}
    {activePresetId}
    canClearFiltersAndSort={Boolean(
      activeKind !== ENTITY_GRID_ALL_KINDS ||
        filterIds.length > 0 ||
        !includeNsfw ||
        query ||
        sortBy !== initialSortBy ||
        sortDir !== initialSortDir ||
        mediaWall !== initialMediaWall ||
        selectedIds.length > 0,
    )}
    {drawerOpen}
    {filterOptions}
    {maxScale}
    {mediaWall}
    {minScale}
    onActiveFilterIdsChange={setFilterIds}
    onApplyPreset={applyPreset}
    onClearFiltersAndSort={clearFiltersAndSort}
    onDeletePreset={deletePreset}
    onDrawerOpenChange={(open) => (drawerOpen = open)}
    onMediaWallChange={setMediaWall}
    onOverwritePreset={overwritePreset}
    onQueryChange={setQuery}
    onSavePreset={savePreset}
    onScaleChange={persistScale}
    onSortByChange={setSortBy}
    onSortDirChange={setSortDir}
    onViewModeChange={setViewMode}
    {presets}
    {query}
    {scale}
    {selectedCount}
    {sortBy}
    {sortDir}
    {viewMode}
  />

  {#if drawerOpen}
    <EntityGridFilterDrawer
      activeFilterIds={filterIds}
      {filterOptions}
      onActiveFilterIdsChange={setFilterIds}
    />
  {/if}

  <EntityGridTabs
    {activeKind}
    onActiveKindChange={setActiveKind}
    {tabs}
    totalCount={cards.length}
  />

  {#if selectedIds.length > 0}
    <div class="bulk-bar" role="status" aria-live="polite">
      <span class="bulk-count">{selectedIds.length} selected</span>
      <div class="bulk-controls">
        <button
          type="button"
          class="bulk-btn"
          title="Select all visible"
          onclick={() => {
            selectedIds = visibleCards.map((c) => c.entity.id);
            onSelectionChange?.(selectedIds);
          }}
        >
          <CheckCheck class="h-3.5 w-3.5" />
          <span class="bulk-btn-label">Select all</span>
        </button>
        <button
          type="button"
          class="bulk-btn"
          title="Clear selection"
          onclick={() => {
            selectedIds = [];
            onSelectionChange?.(selectedIds);
          }}
        >
          <X class="h-3.5 w-3.5" />
          <span class="bulk-btn-label">Clear</span>
        </button>

        <span class="bulk-divider" aria-hidden="true"></span>
        <button
          type="button"
          class="bulk-btn"
          title={allSelectedNsfw ? "Mark SFW" : "Mark NSFW"}
          onclick={() => toggleNsfwFlag(!allSelectedNsfw)}
        >
          <Flame class="h-3.5 w-3.5" />
          <span class="bulk-btn-label">{allSelectedNsfw ? "Mark SFW" : "Mark NSFW"}</span>
        </button>

        {#if bulkActions.length > 0}
          <span class="bulk-divider" aria-hidden="true"></span>
          <div class="bulk-actions-menu">
            <button
              type="button"
              class="bulk-btn"
              class:is-active={actionsMenuOpen}
              title="Actions"
              aria-label="Bulk actions"
              aria-expanded={actionsMenuOpen}
              onclick={() => (actionsMenuOpen = !actionsMenuOpen)}
            >
              <EllipsisVertical class="h-3.5 w-3.5" />
              <span class="bulk-btn-label">Actions</span>
            </button>
            {#if actionsMenuOpen}
              <button
                type="button"
                class="fixed inset-0 z-40 cursor-default"
                aria-label="Close actions menu"
                onclick={() => (actionsMenuOpen = false)}
              ></button>
              <div class="bulk-flyout">
                {#each bulkActions as action (action.id)}
                  <button
                    type="button"
                    class="bulk-flyout-item"
                    class:danger={action.tone === "danger"}
                    onclick={() => {
                      action.onRun(selectedIds);
                      actionsMenuOpen = false;
                    }}
                  >
                    {action.label}
                  </button>
                {/each}
              </div>
            {/if}
          </div>
        {/if}
      </div>
    </div>
  {/if}

  <div
    bind:this={viewportEl}
    class={["grid-viewport", containsScroll && "is-contained"]}
    style:--entity-grid-scroll-max-height={effectiveScrollMaxHeight ?? undefined}
    onwheel={markScrolling}
  >
    {#if loading}
      <div class="loading-grid" aria-label="Loading entities" aria-busy="true">
        {#each Array.from({ length: 12 }) as _, index (index)}
          <div class="skeleton-card">
            <div class="skeleton-media"></div>
            <div class="skeleton-body">
              <span></span>
              <small></small>
              <em></em>
            </div>
          </div>
        {/each}
      </div>
    {:else if visibleCards.length > 0}
      <div class="cards" class:is-list={viewMode === "list"} class:is-media-wall={mediaWall} aria-label="Entities">
        {#each pagedCards as card (card.entity.id)}
          <EntityThumbnail
            {card}
            layout={viewMode}
            linkable={!onCardActivate}
            mediaOnly={mediaWall}
            onActivate={onCardActivate ? (activatedCard) => onCardActivate(activatedCard, pagedCards) : undefined}
            {hoverPreviewsEnabled}
            {selectable}
            selectMode={selectedCount > 0}
            selected={selectedIds.includes(card.entity.id)}
            onSelectedChange={(selected) => updateSelection(card.entity.id, selected)}
          />
        {/each}
      </div>
    {:else}
      <div class="empty" role="status">
        <span class="empty-icon">
          <SearchX aria-hidden="true" />
        </span>
        <strong>{emptyTitle}</strong>
        <span>{emptyMessage}</span>
      </div>
    {/if}
  </div>

  {#if shouldRenderPagination}
    <div class="pagination-shell">
    <nav class="pagination-bar" aria-label="Entity grid pagination">
        <span
          class="pagination-progress"
          aria-hidden="true"
          style:--progress="{Math.max(0, Math.min(1, pageCount > 1 ? (currentPageIndex + 1) / pageCount : 1)) * 100}%"
        ></span>

        <div class="page-readout" aria-live="polite">
          <span class="readout-range" style:--readout-ch="{readoutPlaceholderWidth}ch">
            <strong>{pageStart + 1}–{pageEnd}</strong>
            <span class="readout-divider">/</span>
            <span class="readout-total">{effectiveTotal}{totalIsExact ? "" : "+"}</span>
          </span>
        </div>

        <div class="transport">
          <button
            type="button"
            class="transport-btn"
            title="First page"
            aria-label="First page"
            disabled={!canPageBack}
            onclick={() => setPageIndex(0)}
          >
            <ChevronsLeft aria-hidden="true" />
          </button>
          <button
            type="button"
            class="transport-btn"
            title="Previous page"
            aria-label="Previous page"
            disabled={!canPageBack}
            onclick={() => setPageIndex(currentPageIndex - 1)}
          >
            <ChevronLeft aria-hidden="true" />
          </button>
          <span class="page-count" aria-hidden="true">
            <span class="page-count-current">{String(currentPageIndex + 1).padStart(String(pageCount).length, "0")}</span>
            <span class="page-count-sep">/</span>
            <span class="page-count-total">{pageCount}</span>
          </span>
          <span class="sr-only">Page {currentPageIndex + 1} / {pageCount}</span>
          <button
            type="button"
            class="transport-btn"
            title="Next page"
            aria-label="Next page"
            disabled={!canPageForward || Boolean(loadMoreError) || loadingMore || pendingAdvanceAfterLoad}
            onclick={() => void goToNextPage()}
          >
            {#if loadingMore || pendingAdvanceAfterLoad}
              <LoaderCircle class="is-spinning" aria-hidden="true" />
            {:else}
              <ChevronRight aria-hidden="true" />
            {/if}
          </button>
          <button
            type="button"
            class="transport-btn"
            title="Last page"
            aria-label="Last page"
            disabled={!canSeekToEnd || Boolean(loadMoreError) || loadingMore || pendingAdvanceAfterLoad}
            onclick={() => void goToLastPage()}
          >
            <ChevronsRight aria-hidden="true" />
          </button>
        </div>

        <div class="page-trailing">
          {#if loadMoreError}
            <button
              type="button"
              class="retry-load"
              onclick={() => {
                if (onLoadMore) void onLoadMore();
              }}
            >
              Try again
            </button>
          {/if}
          <div class="page-size-control">
            <span class="page-size-label">PER PAGE</span>
            <div class="relative">
              <button
                type="button"
                class="page-size-btn"
                aria-label="Per page"
                onclick={() => (pageSizeOpen = !pageSizeOpen)}
              >
                {pageSize}
                <ChevronDown class="h-3 w-3 text-text-disabled ml-1 shrink-0" />
              </button>
              {#if showPageSizeMenu}
                <button
                  type="button"
                  class="fixed inset-0 z-40 cursor-default"
                  aria-label="Close page size menu"
                  onclick={() => (pageSizeOpen = false)}
                ></button>
                <div class="page-size-menu">
                  {#each normalizedPageSizeOptions as option (option)}
                    <button
                      type="button"
                      class={cn("page-size-menu-item", pageSize === option && "is-active")}
                      onclick={() => {
                        setPageSize(option);
                        pageSizeOpen = false;
                      }}
                    >
                      <Check class={cn("h-3 w-3 shrink-0", pageSize === option ? "opacity-100" : "opacity-0")} />
                      {option}
                    </button>
                  {/each}
                </div>
              {/if}
            </div>
          </div>
        </div>
    </nav>
    </div>
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
  .entity-grid > * + * {
    margin-top: 0.85rem;
  }

  .entity-grid > :first-child + * {
    margin-top: 0;
  }

  .entity-grid > .pagination-shell,
  .entity-grid.is-static > .pagination-shell {
    margin-top: 0.85rem;
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

  .cards,
  .loading-grid {
    display: grid;
    grid-template-columns: repeat(
      max(1, min(calc(var(--col-count, 5) - 1), 4)),
      minmax(0, 1fr)
    );
    gap: 0.75rem;
    align-items: start;
    overflow-anchor: none;
    contain: layout;
    transition: grid-template-columns 240ms cubic-bezier(0.4, 0, 0.2, 1);
  }

  .cards.is-list {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }

  .cards.is-media-wall {
    grid-template-columns: repeat(var(--col-count, 5), minmax(0, 1fr));
    gap: clamp(0.25rem, 0.8vw, 0.5rem);
  }

  /*
   * The pagination strip is laid out as a 3-column grid so the centered transport
   * stays perfectly centered regardless of how wide the left readout or right
   * trailing controls grow. The two side columns are `1fr` and use `justify-self`
   * to pin their content to the outer edges; the middle is `auto` so the
   * transport hugs its content but always lands on the geometric centerline.
   */
  /*
   * Pagination is intentionally not sticky. It should not participate in every
   * scroll frame; users only need it once they reach the end of the current
   * page, and keeping it in flow avoids thumbnail edge clipping behind it.
   */
  .pagination-shell {
    position: relative;
    z-index: 4;
    padding-bottom: 0;
    background: transparent;
    pointer-events: auto;
  }

  .pagination-shell::after {
    display: none;
  }

  .entity-grid.is-static :global(.toolbar-shell) {
    position: relative;
    top: auto;
    padding-top: 0;
  }

  .entity-grid.is-static :global(.toolbar-shell::before) {
    display: none;
  }

  .pagination-bar {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
    align-items: center;
    gap: 0.85rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgba(12, 15, 21, 0.96);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    backdrop-filter: blur(16px);
    -webkit-backdrop-filter: blur(16px);
    border-radius: var(--radius-sm, 6px);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    padding: 0.7rem 0.85rem;
    pointer-events: auto;
  }

  .pagination-bar > .page-readout {
    justify-self: start;
  }

  .pagination-bar > .transport {
    justify-self: center;
  }

  .pagination-bar > .page-trailing {
    justify-self: end;
  }

  .page-trailing {
    display: inline-flex;
    align-items: center;
    gap: 0.55rem;
  }

  .pagination-progress {
    position: absolute;
    inset: 0 0 auto 0;
    height: 1px;
    background:
      linear-gradient(
        to right,
        rgb(242 194 106 / 0.85) 0%,
        rgb(242 194 106 / 0.95) calc(var(--progress, 0%) - 0.5%),
        rgb(242 194 106 / 0.15) var(--progress, 0%),
        rgb(242 194 106 / 0.05) 100%
      );
    box-shadow: 0 0 12px rgb(242 194 106 / 0.35);
    pointer-events: none;
    transition: background var(--duration-normal) var(--ease-default);
  }

  .page-readout {
    display: inline-flex;
    align-items: baseline;
    gap: 0.55rem;
    min-width: 0;
    color: var(--color-text-muted);
    font-size: 0.65rem;
    letter-spacing: 0.06em;
    white-space: nowrap;
  }

  .readout-label {
    color: var(--color-text-disabled);
    font-size: 0.58rem;
    font-weight: 600;
    letter-spacing: 0.18em;
  }

  .readout-range {
    display: inline-flex;
    align-items: baseline;
    gap: 0.35rem;
    font-variant-numeric: tabular-nums;
    /*
     * Hold a stable minimum width derived from the widest possible digit count
     * for the current total. Combined with tabular-nums above, this keeps the
     * left readout column from changing width as the user pages forward — so the
     * centered transport stays put even though the displayed numerals grow.
     */
    min-width: var(--readout-ch, 11ch);
  }

  .readout-range strong {
    color: var(--color-text-primary);
    font-size: 0.78rem;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-shadow: 0 0 14px rgb(255 255 255 / 0.06);
  }

  .readout-divider {
    color: var(--color-text-disabled);
  }

  .readout-total {
    color: var(--color-text-muted);
  }

  .transport {
    display: inline-flex;
    justify-self: center;
    align-items: center;
    gap: 0.25rem;
    padding: 0.2rem 0.3rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
  }

  .transport-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 2rem;
    height: 1.85rem;
    border: 1px solid transparent;
    background: transparent;
    color: var(--color-text-muted);
    border-radius: var(--radius-xs, 4px);
    transition:
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .transport-btn:hover:not(:disabled) {
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
  }

  .transport-btn:active:not(:disabled) {
    background: var(--color-surface-4, #1c2235);
  }

  .transport-btn:focus-visible {
    outline: none;
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .transport-btn:disabled {
    cursor: not-allowed;
    color: var(--color-text-disabled);
    opacity: 0.38;
  }

  .transport :global(svg) {
    width: 0.95rem;
    height: 0.95rem;
  }

  .transport :global(.is-spinning) {
    animation: spin 0.85s linear infinite;
    color: var(--color-text-accent-bright);
  }

  .page-count {
    display: inline-flex;
    align-items: baseline;
    gap: 0.25rem;
    padding: 0 0.55rem;
    color: var(--color-text-disabled);
    font-size: 0.7rem;
    font-variant-numeric: tabular-nums;
    letter-spacing: 0.08em;
    white-space: nowrap;
  }

  .page-count-current {
    color: var(--color-text-accent-bright);
    font-size: 0.84rem;
    font-weight: 600;
    text-shadow: 0 0 14px rgb(242 194 106 / 0.5);
  }

  .page-count-sep {
    color: var(--color-text-disabled);
  }

  .page-count-total {
    color: var(--color-text-muted);
  }

  .page-size-control {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    justify-self: end;
    color: var(--color-text-disabled);
    white-space: nowrap;
  }

  .page-size-label {
    font-size: 0.58rem;
    font-weight: 600;
    letter-spacing: 0.18em;
  }

  .page-size-btn {
    display: inline-flex;
    align-items: center;
    justify-content: space-between;
    height: 1.85rem;
    min-width: 4.5rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-1, #0c0f15);
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-primary);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    font-variant-numeric: tabular-nums;
    letter-spacing: 0.04em;
    padding: 0 0.45rem 0 0.65rem;
    transition:
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .page-size-btn:hover,
  .page-size-btn:focus-visible {
    outline: none;
    border-color: var(--color-border-accent, rgba(196, 154, 90, 0.25));
    background: var(--color-surface-2, #101420);
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .page-size-menu {
    position: absolute;
    bottom: calc(100% + 0.3rem);
    right: 0;
    z-index: 50;
    min-width: 6rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgba(12, 15, 21, 0.98);
    backdrop-filter: blur(24px);
    -webkit-backdrop-filter: blur(24px);
    border-radius: var(--radius-sm, 6px);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    padding: 0.3rem 0;
    overflow: hidden;
  }

  .page-size-menu-item {
    display: flex;
    align-items: center;
    gap: 0.55rem;
    width: 100%;
    padding: 0.45rem 0.85rem;
    background: transparent;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    letter-spacing: 0.04em;
    text-align: left;
    transition:
      background-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .page-size-menu-item:hover {
    background: rgba(255, 255, 255, 0.04);
    color: var(--color-text-primary);
  }

  .page-size-menu-item.is-active {
    background: linear-gradient(90deg, rgba(196, 154, 90, 0.15), transparent);
    color: var(--color-text-accent, #f2c26a);
  }

  .retry-load {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 1.85rem;
    border: 1px solid rgb(204 120 128 / 0.4);
    background: rgb(40 18 22 / 0.65);
    color: var(--color-error-text);
    font-size: 0.66rem;
    font-weight: 600;
    letter-spacing: 0.1em;
    padding: 0 0.85rem;
    transition: background var(--duration-fast) var(--ease-default);
  }

  .retry-load:hover {
    background: rgb(54 22 28 / 0.85);
  }

  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }

  .skeleton-card {
    display: grid;
    grid-template-rows: auto 1fr;
    overflow: hidden;
    background: var(--color-surface-1, #0c0f15);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-sm, 6px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
  }

  .skeleton-media {
    aspect-ratio: 16 / 9;
    background:
      linear-gradient(90deg, transparent, rgb(255 255 255 / 0.08), transparent),
      var(--color-surface-2);
    background-size: 200% 100%;
    animation: shimmer 1.15s linear infinite;
  }

  .skeleton-body {
    display: grid;
    gap: 0.55rem;
    padding: 0.75rem;
  }

  .skeleton-body span,
  .skeleton-body small,
  .skeleton-body em {
    display: block;
    height: 0.72rem;
    background: var(--color-surface-3);
    opacity: 0.72;
  }

  .skeleton-body span {
    width: 76%;
    height: 1rem;
  }

  .skeleton-body small {
    width: 54%;
  }

  .skeleton-body em {
    width: 38%;
  }

  @keyframes shimmer {
    from {
      background-position: 100% 0;
    }

    to {
      background-position: -100% 0;
    }
  }

  .empty {
    display: grid;
    gap: 0.35rem;
    min-height: 12rem;
    place-content: center;
    background: var(--color-surface-1, #0c0f15);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-sm, 6px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-muted);
    text-align: center;
  }

  .empty-icon {
    display: grid;
    place-items: center;
    justify-self: center;
    width: 2rem;
    height: 2rem;
    color: var(--color-text-disabled);
  }

  .empty-icon :global(svg) {
    width: 100%;
    height: 100%;
  }

  .bulk-bar {
    position: relative;
    z-index: 3;
    display: flex;
    align-items: center;
    gap: 0.75rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgba(12, 15, 21, 0.96);
    backdrop-filter: blur(16px);
    -webkit-backdrop-filter: blur(16px);
    border-radius: var(--radius-sm, 6px);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    padding: 0.55rem 0.7rem;
    pointer-events: auto;
  }

  .bulk-count {
    color: var(--color-text-accent);
    text-transform: uppercase;
    flex-shrink: 0;
  }

  .bulk-controls {
    display: flex;
    align-items: center;
    gap: 0.35rem;
    margin-left: auto;
  }

  .bulk-divider {
    display: inline-block;
    width: 1px;
    height: 1.1rem;
    background: linear-gradient(
      to bottom,
      transparent,
      rgb(255 255 255 / 0.08),
      transparent
    );
    margin: 0 0.1rem;
  }

  .bulk-btn {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    height: 1.85rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    letter-spacing: 0.04em;
    padding: 0 0.55rem;
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    transition:
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .bulk-btn:hover {
    border-color: var(--color-border-accent, rgba(196, 154, 90, 0.25));
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .bulk-btn.is-active {
    border-color: var(--color-border-accent, rgba(196, 154, 90, 0.25));
    background: var(--color-surface-4, #1c2235);
    color: var(--color-text-accent, #f2c26a);
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .bulk-btn-label {
    display: none;
  }

  @media (min-width: 520px) {
    .bulk-btn-label {
      display: inline;
    }
  }

  .bulk-actions-menu {
    position: relative;
  }

  .bulk-flyout {
    position: absolute;
    right: 0;
    top: calc(100% + 0.3rem);
    z-index: 50;
    min-width: 10rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgb(12, 15, 21);
    border-radius: var(--radius-sm, 6px);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    padding: 0.3rem 0;
    overflow: hidden;
  }

  .bulk-flyout-item {
    display: flex;
    align-items: center;
    gap: 0.55rem;
    width: 100%;
    padding: 0.45rem 0.85rem;
    background: transparent;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    letter-spacing: 0.04em;
    text-align: left;
    transition:
      background-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .bulk-flyout-item:hover {
    background: rgb(255 255 255 / 0.04);
    color: var(--color-text-primary);
  }

  .bulk-flyout-item.danger {
    color: var(--color-text-muted);
  }

  .bulk-flyout-item.danger:hover {
    background: rgb(168 72 80 / 0.12);
    color: var(--color-error-text, #cc7880);
  }

  .empty strong {
    color: var(--color-text-primary);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.1rem;
  }

  .empty span {
    font-size: 0.85rem;
  }

  @media (max-width: 720px) {
    /*
     * Mobile stacks the readout, transport, and trailing controls into three
     * rows. Two-column header (readout + trailing per-page select) keeps the
     * status information visible on small screens; the transport gets its own
     * full-width row so the buttons stay comfortably tappable.
     */
    .pagination-bar {
      grid-template-columns: minmax(0, 1fr) minmax(0, auto);
      grid-template-areas:
        "readout  trailing"
        "transport transport";
      gap: 0.6rem 0.7rem;
      padding: 0.65rem 0.7rem 0.7rem;
    }

    .pagination-bar > .page-readout {
      grid-area: readout;
      font-size: 0.62rem;
    }

    .pagination-bar > .page-trailing {
      grid-area: trailing;
    }

    .pagination-bar > .transport {
      grid-area: transport;
      justify-self: stretch;
      justify-content: space-between;
      padding: 0.25rem 0.35rem;
    }

    .readout-range strong {
      font-size: 0.72rem;
    }

    .readout-range {
      min-width: 0; /* stable centering doesn't apply once the transport is full-width */
    }

    .transport-btn {
      flex: 0 0 auto;
    }

    .page-count {
      flex: 1 1 auto;
      justify-content: center;
      padding: 0 0.25rem;
    }
  }

  @media (min-width: 640px) {
    .cards,
    .loading-grid {
      grid-template-columns: repeat(max(1, min(var(--col-count, 5), 4)), minmax(0, 1fr));
    }
  }

  @media (min-width: 1024px) {
    .cards,
    .loading-grid {
      grid-template-columns: repeat(var(--col-count, 5), minmax(0, 1fr));
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .cards,
    .loading-grid {
      transition: none;
    }
  }
</style>
