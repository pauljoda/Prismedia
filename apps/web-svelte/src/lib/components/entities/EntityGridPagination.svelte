<script lang="ts">
  import {
    ChevronLeft,
    ChevronRight,
    ChevronsLeft,
    ChevronsRight,
    LoaderCircle,
  } from "@lucide/svelte";
  import { Button, Select } from "@prismedia/ui-svelte";

  interface Props {
    canPageBack: boolean;
    canPageForward: boolean;
    canSeekToEnd: boolean;
    currentPageIndex: number;
    effectiveTotal: number;
    loadMoreError?: string | null;
    loadingMore: boolean;
    normalizedPageSizeOptions: number[];
    onFirstPage: () => void;
    onLastPage: () => void | Promise<void>;
    onLoadMore?: () => void | Promise<void>;
    onNextPage: () => void | Promise<void>;
    onPageSizeChange: (value: number) => void;
    onPreviousPage: () => void;
    pageCount: number;
    pageEnd: number;
    pageSize: number;
    pageStart: number;
    pendingAdvanceAfterLoad: boolean;
    readoutPlaceholderWidth: number;
    totalIsExact: boolean;
  }

  let {
    canPageBack,
    canPageForward,
    canSeekToEnd,
    currentPageIndex,
    effectiveTotal,
    loadMoreError = null,
    loadingMore,
    normalizedPageSizeOptions,
    onFirstPage,
    onLastPage,
    onLoadMore,
    onNextPage,
    onPageSizeChange,
    onPreviousPage,
    pageCount,
    pageEnd,
    pageSize,
    pageStart,
    pendingAdvanceAfterLoad,
    readoutPlaceholderWidth,
    totalIsExact,
  }: Props = $props();

  const pageSizeOptions = $derived(
    normalizedPageSizeOptions.map((value) => ({ value: String(value), label: String(value) })),
  );
</script>

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
      <Button
        variant="ghost"
        size="icon"
        title="First page"
        aria-label="First page"
        disabled={!canPageBack}
        onclick={onFirstPage}
      >
        <ChevronsLeft aria-hidden="true" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        title="Previous page"
        aria-label="Previous page"
        disabled={!canPageBack}
        onclick={onPreviousPage}
      >
        <ChevronLeft aria-hidden="true" />
      </Button>
      <span class="page-count" aria-hidden="true">
        <span class="page-count-current">{String(currentPageIndex + 1).padStart(String(pageCount).length, "0")}</span>
        <span class="page-count-sep">/</span>
        <span class="page-count-total">{pageCount}</span>
      </span>
      <span class="sr-only">Page {currentPageIndex + 1} / {pageCount}</span>
      <Button
        variant="ghost"
        size="icon"
        title="Next page"
        aria-label="Next page"
        disabled={!canPageForward || Boolean(loadMoreError) || loadingMore || pendingAdvanceAfterLoad}
        onclick={() => void onNextPage()}
      >
        {#if loadingMore || pendingAdvanceAfterLoad}
          <LoaderCircle class="is-spinning" aria-hidden="true" />
        {:else}
          <ChevronRight aria-hidden="true" />
        {/if}
      </Button>
      <Button
        variant="ghost"
        size="icon"
        title="Last page"
        aria-label="Last page"
        disabled={!canSeekToEnd || Boolean(loadMoreError) || loadingMore || pendingAdvanceAfterLoad}
        onclick={() => void onLastPage()}
      >
        <ChevronsRight aria-hidden="true" />
      </Button>
    </div>

    <div class="page-trailing">
      {#if loadMoreError}
        <Button
          variant="danger"
          size="sm"
          onclick={() => {
            if (onLoadMore) void onLoadMore();
          }}
        >
          Try again
        </Button>
      {/if}
      <div class="page-size-control">
        <span class="page-size-label">PER PAGE</span>
        <Select
          ariaLabel="Per page"
          size="sm"
          class="w-auto min-w-18"
          options={pageSizeOptions}
          value={String(pageSize)}
          onchange={(value) => {
            const option = normalizedPageSizeOptions.find((size) => String(size) === value);
            if (option !== undefined) onPageSizeChange(option);
          }}
        />
      </div>
    </div>
  </nav>
</div>

<style>
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

  /*
   * The pagination strip is laid out as a 3-column grid so the centered transport
   * stays perfectly centered regardless of how wide the left readout or right
   * trailing controls grow.
   */
  .pagination-bar {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
    align-items: center;
    gap: 0.85rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgba(12, 15, 21, 0.96);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    backdrop-filter: blur(var(--glass-blur-md));
    -webkit-backdrop-filter: blur(var(--glass-blur-md));
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
        rgb(199 201 204 / 0.85) 0%,
        rgb(199 201 204 / 0.95) calc(var(--progress, 0%) - 0.5%),
        rgb(199 201 204 / 0.15) var(--progress, 0%),
        rgb(199 201 204 / 0.05) 100%
      );
    box-shadow: 0 0 12px rgb(199 201 204 / 0.35);
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

  .readout-range {
    display: inline-flex;
    align-items: baseline;
    gap: 0.35rem;
    font-variant-numeric: tabular-nums;
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
    text-shadow: 0 0 14px rgb(199 201 204 / 0.5);
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

  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }

  @media (max-width: 720px) {
    /*
     * Mobile keeps the readout and trailing controls above a full-width transport.
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
      min-width: 0;
    }

    .page-count {
      flex: 1 1 auto;
      justify-content: center;
      padding: 0 0.25rem;
    }
  }
</style>
