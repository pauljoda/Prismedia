<script lang="ts">
  import { browser } from "$app/environment";
  import {
    SearchX,
  } from "@lucide/svelte";
  import { onMount } from "svelte";
  import { isNsfw, withFlagCapability } from "$lib/api/capabilities";
  import type { EntityCapability } from "$lib/api/generated/model";
  import { updateEntityFlags } from "$lib/api/entity-mutations";
  import { createFilterPresets, type FilterPreset } from "$lib/filter-presets";
  import { createEntityGridPrefs, type EntityGridPrefs } from "$lib/entities/entity-grid-prefs";
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
  import EntityGridPagination from "./EntityGridPagination.svelte";
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
    /**
     * The single entity kind this grid is browsing, when known. Drives adaptive
     * filter labels (e.g. Read/Unread for books vs Watched/Unwatched for video).
     */
    entityKind?: string;
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
    entityKind,
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

  function presetStorageKey(): string | null {
    return prefsKey ? `prismedia:entity-grid-presets:${prefsKey}` : null;
  }

  function isMobileThumbnailViewport(): boolean {
    return browser &&
      typeof window.matchMedia === "function" &&
      window.matchMedia(MOBILE_THUMBNAIL_QUERY).matches;
  }

  function defaultScale(): number {
    return isMobileThumbnailViewport() ? minScale : DEFAULT_SCALE;
  }

  function clampScale(value: number): number {
    return Math.min(maxScale, Math.max(minScale, value));
  }

  function normalizePageSize(value: number): number {
    const numeric = Math.floor(value);
    return Number.isFinite(numeric) && numeric > 0 ? numeric : DEFAULT_PAGE_SIZE;
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
        pageSize: normalizePageSize(initialPageSize),
      })
    : null;
  const persistedPrefs: EntityGridPrefs | null = prefsStore ? prefsStore.load() : null;

  let capabilityOverrides = $state(new Map<string, EntityCapability[]>());
  let activeKind = $state(persistedPrefs?.activeKind ?? ENTITY_GRID_ALL_KINDS);
  let activePresetId = $state<string | null>(persistedPrefs?.activePresetId ?? null);
  let drawerOpen = $state(false);
  let filterIds = $state<string[]>(persistedPrefs?.filterIds ?? []);
  let includeNsfw = $state(persistedPrefs?.includeNsfw ?? true);
  let presets = $state<FilterPreset[]>([]);
  let query = $state(persistedPrefs?.query ?? "");
  let pageIndex = $state(0);
  // svelte-ignore state_referenced_locally
  let pageSize = $state(persistedPrefs?.pageSize ?? normalizePageSize(initialPageSize));
  let pendingAdvanceAfterLoad = $state(false);
  // svelte-ignore state_referenced_locally
  let scale = $state(persistedPrefs ? clampScale(persistedPrefs.scale) : defaultScale());
  // svelte-ignore state_referenced_locally
  let mediaWall = $state(persistedPrefs?.mediaWall ?? initialMediaWall);
  let selectedIds = $state<string[]>([]);
  // Selection is explicit: until the user turns it on, cards behave as plain links/activators
  // (a single tap navigates). Turning it on reveals the checkboxes and routes taps to selection.
  let selectionActive = $state(false);
  let viewportEl: HTMLDivElement | undefined = $state();
  let sectionEl: HTMLElement | undefined = $state();
  let measuredScrollMaxHeight = $state<string | null>(null);
  let measuredFillHeight = $state<string | null>(null);
  let hoverPreviewsResumeAt = 0;
  // svelte-ignore state_referenced_locally
  let sortBy = $state<EntityGridSort>(persistedPrefs?.sortBy ?? initialSortBy);
  // svelte-ignore state_referenced_locally
  let sortDir = $state<EntityGridSortDir>(persistedPrefs?.sortDir ?? initialSortDir);
  // Seed for the random sort. Regenerated each time Random is (re)selected so the
  // shuffle changes, but held stable across pagination within one shuffle.
  let randomSeed = $state(1);
  let viewMode = $state<EntityGridViewMode>(persistedPrefs?.viewMode ?? "grid");
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
    randomSeed?: number;
    viewMode: EntityGridViewMode;
    mediaWall?: boolean;
    selectedIds: string[];
    scale: number;
    pageIndex: number;
    pageSize: number;
  }

  const pageSnapshots = usePageSnapshots();

  function findScrollAncestor(el: Element): Element | null {
    let current: Element | null = el.parentElement;
    while (current && current !== document.body && current !== document.documentElement) {
      const cs = getComputedStyle(current);
      const overflows = cs.overflowY === "auto" || cs.overflowY === "scroll";
      if (overflows) return current;
      current = current.parentElement;
    }
    return null;
  }

  onMount(() => {
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
        randomSeed,
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
        randomSeed = snapshot.randomSeed ?? randomSeed;
        viewMode = snapshot.viewMode;
        mediaWall = snapshot.mediaWall ?? initialMediaWall;
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
      return findScrollAncestor(el)?.getBoundingClientRect().bottom ?? window.innerHeight;
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

  function areHoverPreviewsSuppressed() {
    return browser && performance.now() < hoverPreviewsResumeAt;
  }

  function markScrolling() {
    hoverPreviewsResumeAt = performance.now() + 220;
  }

  onMount(() => {
    window.addEventListener("scroll", markScrolling, { capture: true, passive: true });

    return () => {
      window.removeEventListener("scroll", markScrolling, { capture: true });
    };
  });

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
      pageSize,
      activePresetId,
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

  /** Generates a fresh, non-zero seed for the random shuffle. */
  function nextRandomSeed(): number {
    return Math.floor(Math.random() * 2_000_000_000) + 1;
  }

  function setSortBy(value: EntityGridSort) {
    // Re-selecting Random reshuffles; selecting it for the first time seeds it.
    if (value === "random") {
      randomSeed = nextRandomSeed();
      pageIndex = 0;
    }
    sortBy = value;
    activePresetId = null;
  }

  /** Reshuffles the current random ordering with a new seed. */
  function reshuffle() {
    randomSeed = nextRandomSeed();
    pageIndex = 0;
  }

  function setSortDir(value: EntityGridSortDir) {
    sortDir = value;
    activePresetId = null;
  }

  function setViewMode(value: EntityGridViewMode) {
    viewMode = value;
    if (value === "list") {
      mediaWall = false;
    }
  }

  function setMediaWall(value: boolean) {
    mediaWall = value;
    if (value) viewMode = "grid";
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
    const presetSorts: EntityGridSort[] = ["title", "kind", "rating", "position", "added", "random"];
    sortBy = (presetSorts as string[]).includes(preset.sortBy) ? (preset.sortBy as EntityGridSort) : initialSortBy;
    if (sortBy === "random") randomSeed = nextRandomSeed();
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
    filterIds = [];
    includeNsfw = true;
    query = "";
    selectedIds = [];
    sortBy = initialSortBy;
    sortDir = initialSortDir;
    viewMode = "grid";
    mediaWall = initialMediaWall;
    pageIndex = 0;
    onSelectionChange?.(selectedIds);
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
    if (!browser || !viewportEl) return;
    const scrollAncestor = findScrollAncestor(viewportEl);
    if (scrollAncestor instanceof HTMLElement) {
      const ancestorRect = scrollAncestor.getBoundingClientRect();
      const viewportRect = viewportEl.getBoundingClientRect();
      scrollAncestor.scrollTo({
        top: scrollAncestor.scrollTop + viewportRect.top - ancestorRect.top,
      });
      return;
    }

    window.scrollTo({
      top: window.scrollY + viewportEl.getBoundingClientRect().top,
    });
  }

  function setPageIndex(next: number) {
    pageIndex = Math.max(0, Math.min(pageCount - 1, next));
    queueMicrotask(scrollPageToTop);
  }

  function setPageSize(value: number) {
    pageSize = normalizePageSize(value);
    pageIndex = 0;
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

{#snippet ThumbnailCard(card: EntityThumbnailCard)}
  <EntityThumbnail
    {card}
    imageFetchPriority="auto"
    imageLoading="lazy"
    layout={viewMode}
    linkable={!onCardActivate}
    mediaOnly={mediaWall}
    onActivate={onCardActivate ? (activatedCard) => onCardActivate(activatedCard, pagedCards) : undefined}
    hoverPreviewSuppressed={areHoverPreviewsSuppressed}
    selectable={selectable && selectionActive}
    selectMode={selectionActive}
    selected={selectedIds.includes(card.entity.id)}
    onSelectedChange={(selected) => updateSelection(card.entity.id, selected)}
  />
{/snippet}

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
    {allSelectedNsfw}
    {bulkActions}
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

  {#if drawerOpen}
    <EntityGridFilterDrawer
      activeFilterIds={filterIds}
      {filterOptions}
      {entityKind}
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
      <div
        class="cards"
        class:is-list={viewMode === "list"}
        class:is-media-wall={mediaWall}
        aria-label="Entities"
      >
        {#each pagedCards as card (card.entity.id)}
          {@render ThumbnailCard(card)}
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
    <EntityGridPagination
      {canPageBack}
      {canPageForward}
      {canSeekToEnd}
      {currentPageIndex}
      {effectiveTotal}
      {loadMoreError}
      {loadingMore}
      {normalizedPageSizeOptions}
      onFirstPage={() => setPageIndex(0)}
      onLastPage={goToLastPage}
      {onLoadMore}
      onNextPage={goToNextPage}
      onPageSizeChange={setPageSize}
      onPreviousPage={() => setPageIndex(currentPageIndex - 1)}
      {pageCount}
      {pageEnd}
      {pageSize}
      {pageStart}
      {pendingAdvanceAfterLoad}
      {readoutPlaceholderWidth}
      {totalIsExact}
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

  .entity-grid.is-static :global(.toolbar-shell) {
    position: relative;
    top: auto;
    padding-top: 0;
  }

  .entity-grid.is-static :global(.toolbar-shell::before) {
    display: none;
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
    justify-items: center;
    padding: 2.5rem 1.25rem;
    background: var(--color-surface-1, #0c0f15);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-sm, 6px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-muted);
    text-align: center;
  }

  .empty > strong,
  .empty > span:not(.empty-icon) {
    max-width: 32rem;
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

  .empty strong {
    color: var(--color-text-primary);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.1rem;
  }

  .empty span {
    font-size: 0.85rem;
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
