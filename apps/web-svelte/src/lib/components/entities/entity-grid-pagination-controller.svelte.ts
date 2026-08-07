export interface EntityGridPaginationControllerOptions {
  initialPageSize: number;
  pageSizeOptions: () => number[];
  sourceCount: () => number;
  visibleCount: () => number;
  hasMore: () => boolean;
  loading: () => boolean;
  loadingMore: () => boolean;
  loadMoreError: () => string | null;
  remoteTotalCount: () => number | null;
  showPagination: () => boolean;
  onLoadMore: () => (() => void | Promise<void>) | undefined;
  onPageSizeChange: () => ((pageSize: number) => void) | undefined;
  onNavigate: () => void;
}

/** Normalizes a caller- or preference-supplied EntityGrid page size. */
export function normalizeEntityGridPageSize(value: number): number {
  const numeric = Math.floor(value);
  return Number.isFinite(numeric) && numeric > 0 ? numeric : 100;
}

/**
 * Owns EntityGrid paging, remote buffering, page-size changes, and pagination presentation facts.
 * The public EntityGrid remains the composition root while this controller keeps the independent
 * paging state machine out of its view and selection/filter concerns.
 */
export class EntityGridPaginationController {
  pageIndex = $state(0);
  pageSize = $state(100);
  pendingAdvanceAfterLoad = $state(false);

  constructor(private readonly options: EntityGridPaginationControllerOptions) {
    this.pageSize = normalizeEntityGridPageSize(options.initialPageSize);
  }

  get normalizedPageSizeOptions(): number[] {
    return Array.from(new Set(
      [...this.options.pageSizeOptions(), this.pageSize].map(normalizeEntityGridPageSize),
    )).sort((left, right) => left - right);
  }

  get paginationThreshold(): number {
    return this.normalizedPageSizeOptions[0] ?? this.pageSize;
  }

  get isLocallyFiltered(): boolean {
    return this.options.visibleCount() !== this.options.sourceCount();
  }

  get knownRemoteTotal(): number | null {
    const total = this.options.remoteTotalCount();
    return total != null && total >= 0 ? total : null;
  }

  get effectiveTotal(): number {
    if (this.isLocallyFiltered) return this.options.visibleCount();
    if (this.knownRemoteTotal != null) return this.knownRemoteTotal;
    return this.options.sourceCount() + (this.options.hasMore() ? 1 : 0);
  }

  get totalIsExact(): boolean {
    return this.isLocallyFiltered ? !this.options.hasMore() : this.knownRemoteTotal != null;
  }

  get pageCount(): number {
    return Math.max(1, Math.ceil(this.effectiveTotal / this.pageSize));
  }

  get currentPageIndex(): number {
    return Math.min(this.pageIndex, this.pageCount - 1);
  }

  get pageStart(): number {
    return this.effectiveTotal === 0 ? 0 : this.currentPageIndex * this.pageSize;
  }

  get pageEnd(): number {
    return Math.min(this.effectiveTotal, this.pageStart + this.pageSize);
  }

  get canPageBack(): boolean {
    return this.currentPageIndex > 0;
  }

  get canPageForward(): boolean {
    return this.currentPageIndex < this.pageCount - 1 ||
      Boolean(this.options.hasMore() && this.options.onLoadMore());
  }

  get canSeekToEnd(): boolean {
    return this.currentPageIndex < this.pageCount - 1;
  }

  get shouldRender(): boolean {
    return this.options.showPagination() &&
      !this.options.loading() &&
      this.options.visibleCount() > 0 &&
      (this.effectiveTotal > this.paginationThreshold ||
        this.pageCount > 1 ||
        this.currentPageIndex > 0 ||
        this.options.hasMore() ||
        Boolean(this.options.loadMoreError()));
  }

  /** Widest possible readout string, used to reserve a stable pagination layout slot. */
  get readoutPlaceholderWidth(): number {
    return Math.max(
      String(this.effectiveTotal).length,
      String(this.pageStart + 1).length,
      String(this.pageEnd).length,
    ) * 2 + 4;
  }

  /** Returns only the current page without owning or copying the full visible collection. */
  page<T>(items: T[]): T[] {
    return items.slice(this.pageStart, Math.min(items.length, this.pageStart + this.pageSize));
  }

  /** Announces the initial/restored page size to a server-backed grid. */
  notifyPageSize(): void {
    this.options.onPageSizeChange()?.(this.pageSize);
  }

  /** Restores paging state without forcing a scroll jump. */
  restore(pageIndex: number, pageSize: number): void {
    this.pageSize = normalizeEntityGridPageSize(pageSize);
    this.pageIndex = Math.max(0, pageIndex);
    this.notifyPageSize();
  }

  resetPage(): void {
    this.pageIndex = 0;
  }

  setPageIndex = (next: number): void => {
    this.pageIndex = Math.max(0, Math.min(this.pageCount - 1, next));
    this.navigateToPage();
  };

  setPageSize = (value: number): void => {
    this.pageSize = normalizeEntityGridPageSize(value);
    this.pageIndex = 0;
    this.notifyPageSize();
    this.navigateToPage();
  };

  goToNextPage = async (): Promise<void> => {
    if (this.currentPageIndex < this.pageCount - 1) {
      const targetPage = this.currentPageIndex + 1;
      if (this.options.visibleCount() > targetPage * this.pageSize || !this.options.hasMore()) {
        this.setPageIndex(targetPage);
        return;
      }
      await this.loadAndAdvance(targetPage);
      return;
    }

    if (!this.options.hasMore() || !this.options.onLoadMore() || this.options.loadingMore()) return;
    await this.loadAndAdvance(this.currentPageIndex + 1);
  };

  goToLastPage = async (): Promise<void> => {
    const lastPage = this.pageCount - 1;
    if (lastPage <= this.currentPageIndex) return;
    if (this.options.visibleCount() > lastPage * this.pageSize || !this.options.hasMore()) {
      this.setPageIndex(lastPage);
      return;
    }

    this.pendingAdvanceAfterLoad = true;
    try {
      await this.ensurePageLoaded(lastPage);
      this.setPageIndex(Math.min(lastPage, this.pageCount - 1));
    } finally {
      this.pendingAdvanceAfterLoad = false;
    }
  };

  private async loadAndAdvance(targetPage: number): Promise<void> {
    this.pendingAdvanceAfterLoad = true;
    try {
      await this.ensurePageLoaded(targetPage);
      this.setPageIndex(targetPage);
    } finally {
      this.pendingAdvanceAfterLoad = false;
    }
  }

  /** Loads enough remote cursor pages to make one target page renderable. */
  private async ensurePageLoaded(targetPage: number): Promise<void> {
    const loadMore = this.options.onLoadMore();
    if (!this.options.hasMore() || !loadMore) return;

    const targetStart = targetPage * this.pageSize;
    while (this.options.visibleCount() <= targetStart && this.options.hasMore()) {
      const previousCount = this.options.visibleCount();
      await loadMore();
      if (this.options.visibleCount() <= previousCount) break;
    }
  }

  private navigateToPage(): void {
    queueMicrotask(this.options.onNavigate);
  }
}
