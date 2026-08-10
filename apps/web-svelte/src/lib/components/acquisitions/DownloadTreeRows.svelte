<script lang="ts">
  import Self from "./DownloadTreeRows.svelte";
  import { ChevronRight } from "@lucide/svelte";
  import { Badge, Button, Checkbox, type BadgeVariant } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import type { AcquisitionListItem, AcquisitionItemTone } from "$lib/requests/acquisition-list-item";
  import { formatBytes, formatEta, formatRelativeTime, formatSpeed, numberValue } from "$lib/utils/format";
  import type { DownloadManagerEntry, DownloadTreeNode } from "./download-tree";

  let {
    nodes,
    entriesById,
    expanded,
    selectedKey,
    checkedIds,
    columnTemplate,
    depth = 0,
    onToggleExpanded,
    onSelect,
    onSetChecked,
  }: {
    nodes: DownloadTreeNode[];
    entriesById: ReadonlyMap<string, DownloadManagerEntry>;
    expanded: ReadonlySet<string>;
    selectedKey: string | null;
    checkedIds: ReadonlySet<string>;
    columnTemplate: string;
    depth?: number;
    onToggleExpanded: (key: string) => void;
    onSelect: (id: string) => void;
    onSetChecked: (ids: string[], checked: boolean) => void;
  } = $props();

  interface NodeSummary {
    count: number;
    progress: number | null;
    totalSize: number;
    speed: number;
    eta: number;
    seeds: number;
    peers: number;
    updatedAt: string | null;
    tone: AcquisitionItemTone;
  }

  const tonePriority: Record<AcquisitionItemTone, number> = {
    failed: 8,
    attention: 7,
    cleanup: 6,
    downloading: 5,
    searching: 4,
    queued: 3,
    muted: 2,
    done: 1,
  };

  function summaryFor(node: DownloadTreeNode): NodeSummary {
    const entries = node.descendantEntryIds
      .map((id) => entriesById.get(id))
      .filter((entry): entry is DownloadManagerEntry => entry !== undefined);
    const progress = entries
      .map((entry) => entry.item.progress)
      .filter((value): value is number => value !== null);
    return {
      count: entries.length,
      progress: progress.length > 0 ? progress.reduce((sum, value) => sum + value, 0) / progress.length : null,
      totalSize: entries.reduce((sum, entry) => sum + (numberValue(entry.row.totalSizeBytes) ?? 0), 0),
      speed: entries.reduce((sum, entry) => sum + (numberValue(entry.row.downloadSpeedBytesPerSecond) ?? 0), 0),
      eta: entries.reduce((longest, entry) => Math.max(longest, numberValue(entry.row.etaSeconds) ?? 0), 0),
      seeds: entries.reduce((sum, entry) => sum + (numberValue(entry.row.seeds) ?? 0), 0),
      peers: entries.reduce((sum, entry) => sum + (numberValue(entry.row.peers) ?? 0), 0),
      updatedAt: entries.reduce<string | null>((latest, entry) =>
        !latest || entry.row.updatedAt > latest ? entry.row.updatedAt : latest, null),
      tone: entries.reduce<AcquisitionItemTone>((tone, entry) =>
        tonePriority[entry.item.tone] > tonePriority[tone] ? entry.item.tone : tone, "done"),
    };
  }

  function badgeVariant(tone: AcquisitionItemTone): BadgeVariant {
    if (tone === "failed") return "error";
    if (tone === "attention") return "warning";
    if (tone === "done") return "success";
    if (tone === "downloading" || tone === "searching") return "accent";
    return "default";
  }

  function entryThumbnail(node: DownloadTreeNode, entry: DownloadManagerEntry | null) {
    if (node.thumbnail) {
      return { ...entityCardToThumbnailCard(node.thumbnail), aspectRatio: "square" as const };
    }
    return entry?.item.thumbnail ?? null;
  }
</script>

{#snippet managerRow(node: DownloadTreeNode, entry: DownloadManagerEntry | null, rowDepth: number, extra = false)}
  {@const summary = summaryFor(node)}
  {@const hasChildren = !extra && (node.children.length > 0 || node.directEntryIds.length > 1)}
  {@const isGroupRow = hasChildren}
  {@const isExpanded = expanded.has(node.key)}
  {@const selectionKey = isGroupRow ? node.key : entry?.item.id ?? node.key}
  {@const selectableIds = isGroupRow
    ? node.descendantEntryIds.filter((id) => entriesById.get(id)?.item.selectable !== false)
    : entry?.item.selectable !== false && entry ? [entry.item.id] : []}
  {@const allChecked = selectableIds.length > 0 && selectableIds.every((id) => checkedIds.has(id))}
  {@const someChecked = selectableIds.some((id) => checkedIds.has(id))}
  {@const thumbnail = entryThumbnail(node, entry)}
  {@const progress = entry?.item.progress ?? summary.progress}
  {@const percent = progress === null ? null : Math.round(Math.min(1, Math.max(0, progress)) * 100)}
  {@const row = entry?.row ?? null}
  {@const tone = entry?.item.tone ?? summary.tone}
  {@const statusLabel = entry?.item.statusLabel ?? `${summary.count} ${summary.count === 1 ? "download" : "downloads"}`}
  {@const totalSize = row ? numberValue(row.totalSizeBytes) ?? 0 : summary.totalSize}
  {@const speed = row ? numberValue(row.downloadSpeedBytesPerSecond) ?? 0 : summary.speed}
  {@const eta = row ? numberValue(row.etaSeconds) ?? 0 : summary.eta}
  {@const seeds = row ? numberValue(row.seeds) ?? 0 : summary.seeds}
  {@const peers = row ? numberValue(row.peers) ?? 0 : summary.peers}
  {@const updatedAt = row?.updatedAt ?? summary.updatedAt}
  {@const title = extra && entry ? entry.item.title : node.title}

  <div
    role="row"
    aria-selected={selectionKey === selectedKey}
    class={[
      "download-row",
      selectionKey === selectedKey && "is-selected",
      isGroupRow && "is-group",
    ]}
    style:grid-template-columns={columnTemplate}
    style:--tree-depth={rowDepth}
  >
    <Button
      variant="ghost"
      class="row-hit"
      aria-label={isGroupRow ? `Inspect ${node.title} downloads` : `Inspect ${entry?.item.title ?? node.title}`}
      onclick={() => onSelect(selectionKey)}
    />

    <div role="gridcell" class="select-cell interactive-cell">
      {#if selectableIds.length > 0}
        <Checkbox
          size="md"
          checked={allChecked}
          indeterminate={someChecked && !allChecked}
          onchange={() => onSetChecked(selectableIds, !allChecked)}
          aria-label={isGroupRow ? `Select all ${node.title} downloads` : `Select ${entry?.item.title ?? node.title}`}
        />
      {/if}
    </div>

    <div role="gridcell" class="name-cell">
      <span class="tree-indent" aria-hidden="true"></span>
      <span class="interactive-cell expander-slot">
        {#if hasChildren}
          <Button
            variant="ghost"
            size="icon"
            class="expander"
            aria-label={`${isExpanded ? "Collapse" : "Expand"} ${node.title}`}
            aria-expanded={isExpanded}
            onclick={() => onToggleExpanded(node.key)}
          >
            <ChevronRight class={["chevron h-3.5 w-3.5", isExpanded && "is-open"]} />
          </Button>
        {:else}
          <span class="branch-mark" aria-hidden="true"></span>
        {/if}
      </span>
      {#if thumbnail}
        <span class="row-artwork">
          <EntityThumbnail
            card={thumbnail}
            mediaOnly
            interactive={false}
            hoverPreviewsEnabled={false}
            showWantedBadge={false}
            artworkReactive={false}
          />
        </span>
      {/if}
      <span class="identity-copy">
        <span class="row-title">{title}</span>
        <span class="row-subtitle">
          {#if entry?.item.subtitle}{entry.item.subtitle}{:else if node.thumbnail}{labelForEntityKind(node.thumbnail.kind)}{:else}Unbound download{/if}
          {#if !entry && summary.count > 0}<span> · {summary.count} active</span>{/if}
        </span>
      </span>
    </div>

    <div role="gridcell" class="size-cell mono-cell">{formatBytes(totalSize)}</div>
    <div role="gridcell" class="progress-cell">
      <div class="progress-reading">
        <span class="progress-track" aria-hidden="true">
          <span
            class:indeterminate={entry?.item.indeterminate === true}
            class="progress-fill"
            style:width={entry?.item.indeterminate ? "38%" : `${percent ?? 0}%`}
          ></span>
        </span>
        <span class="progress-value">{percent === null ? "—" : `${percent}%`}</span>
      </div>
    </div>
    <div role="gridcell" class="status-cell">
      <Badge variant={badgeVariant(tone)} class="status-badge">{statusLabel}</Badge>
      {#if entry?.item.clientLabel}<span class="client-label">{entry.item.clientLabel}</span>{/if}
    </div>
    <div role="gridcell" class="speed-cell mono-cell">{formatSpeed(speed)}</div>
    <div role="gridcell" class="eta-cell mono-cell">{formatEta(eta)}</div>
    <div role="gridcell" class="peers-cell mono-cell">{seeds || peers ? `${seeds} / ${peers}` : "—"}</div>
    <div role="gridcell" class="updated-cell mono-cell">{updatedAt ? formatRelativeTime(updatedAt, true) : "—"}</div>
  </div>
{/snippet}

{#each nodes as node (node.key)}
  {@const directEntries = node.directEntryIds
    .map((id) => entriesById.get(id))
    .filter((entry): entry is DownloadManagerEntry => entry !== undefined)}
  {@const nodeIsGroup = node.children.length > 0 || directEntries.length > 1}
  {@const primaryEntry = nodeIsGroup ? null : directEntries[0] ?? null}
  {@render managerRow(node, primaryEntry, depth)}

  {#if expanded.has(node.key)}
    {#each (nodeIsGroup ? directEntries : directEntries.slice(1)) as entry (entry.item.id)}
      {@render managerRow(node, entry, depth + 1, true)}
    {/each}
    <Self
      nodes={node.children}
      {entriesById}
      {expanded}
      {selectedKey}
      {checkedIds}
      {columnTemplate}
      depth={depth + 1}
      {onToggleExpanded}
      {onSelect}
      {onSetChecked}
    />
  {/if}
{/each}

<style>
  .download-row {
    position: relative;
    display: grid;
    min-width: 100%;
    min-height: 3.5rem;
    align-items: stretch;
    border-bottom: 1px solid var(--color-border-subtle);
    color: var(--color-text-secondary);
    transition: background 120ms ease, box-shadow 120ms ease;
  }

  .download-row:last-child { border-bottom-color: transparent; }
  .download-row.is-group { background: rgb(255 255 255 / 0.014); }
  .download-row.is-selected {
    background: color-mix(in srgb, var(--color-accent-500, #c7c9cc) 8%, var(--color-surface-1));
    box-shadow: inset 0 -2px 0 var(--color-accent-400, #c7c9cc);
  }

  :global(.row-hit) {
    position: absolute;
    inset: 0;
    z-index: 0;
    width: 100%;
    height: 100%;
    padding: 0;
    border: 0;
    border-radius: 0;
    background: transparent;
    box-shadow: none;
  }
  :global(.row-hit:hover) { background: rgb(255 255 255 / 0.025); }
  :global(.row-hit:focus-visible) { box-shadow: inset 0 0 0 2px rgb(199 201 204 / 0.34); }

  [role="gridcell"] {
    position: relative;
    z-index: 1;
    display: flex;
    min-width: 0;
    align-items: center;
    padding: 0.45rem 0.7rem;
    border-right: 1px solid rgb(255 255 255 / 0.045);
    pointer-events: none;
  }
  [role="gridcell"]:last-child { border-right: 0; }
  .interactive-cell { z-index: 2; pointer-events: auto; }
  .select-cell { justify-content: center; padding-inline: 0.35rem; }

  .name-cell { gap: 0.55rem; padding-left: 0.35rem; }
  .tree-indent { flex: 0 0 calc(var(--tree-depth) * 1.05rem); }
  .expander-slot { display: grid; flex: 0 0 1.65rem; place-items: center; }
  :global(.expander) { width: 1.65rem; height: 1.65rem; padding: 0; color: var(--color-text-muted); }
  :global(.chevron) { transition: transform 140ms ease; }
  :global(.chevron.is-open) { transform: rotate(90deg); }
  .branch-mark { width: 0.4rem; height: 1px; background: var(--color-border-default); }

  .row-artwork {
    flex: 0 0 2.45rem;
    width: 2.45rem;
    overflow: hidden;
    border-radius: var(--radius-xs, 4px);
    box-shadow: 0 0 0 1px var(--color-border-subtle), 0 2px 8px rgb(0 0 0 / 0.32);
  }
  .row-artwork :global(.media) { border: 0; border-radius: var(--radius-xs, 4px); box-shadow: none; }
  .identity-copy { display: flex; min-width: 0; flex-direction: column; gap: 0.15rem; }
  .row-title { overflow: hidden; color: var(--color-text-primary); font-family: var(--font-heading, "Geist", sans-serif); font-size: 0.83rem; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
  .row-subtitle { overflow: hidden; color: var(--color-text-muted); font-size: 0.67rem; text-overflow: ellipsis; white-space: nowrap; }
  .mono-cell { justify-content: flex-end; font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-variant-numeric: tabular-nums; }

  .progress-reading { display: flex; width: 100%; align-items: center; gap: 0.55rem; }
  .progress-track { position: relative; height: 0.32rem; flex: 1 1 auto; overflow: hidden; border-radius: 2px; background: var(--color-surface-4); box-shadow: inset 0 1px 2px rgb(0 0 0 / 0.45); }
  .progress-fill { position: absolute; inset-block: 0; left: 0; border-radius: inherit; background: var(--color-accent-400, #c7c9cc); box-shadow: 0 0 8px rgb(199 201 204 / 0.16); transition: width 240ms ease; }
  .progress-fill.indeterminate { animation: transfer-sweep 1.35s ease-in-out infinite alternate; }
  .progress-value { flex: 0 0 2.5rem; text-align: right; font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-variant-numeric: tabular-nums; }
  .status-cell { flex-direction: column; align-items: flex-start; justify-content: center; gap: 0.15rem; }
  :global(.status-badge) { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.65rem; }
  .client-label { max-width: 100%; overflow: hidden; color: var(--color-text-muted); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.61rem; text-overflow: ellipsis; white-space: nowrap; }

  @keyframes transfer-sweep { from { transform: translateX(-85%); } to { transform: translateX(165%); } }

  @media (prefers-reduced-motion: reduce) {
    :global(.chevron), .progress-fill { transition: none; }
    .progress-fill.indeterminate { animation: none; width: 100% !important; opacity: 0.45; }
  }
</style>
