<script lang="ts">
  import { ChevronRight, ExternalLink, Layers3 } from "@lucide/svelte";
  import { Badge, Button, type BadgeVariant } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { displayNameForEntityKind } from "$lib/entities/entity-codes";
  import { formatBytes, formatEta, formatSpeed, numberValue } from "$lib/utils/format";
  import type { AcquisitionItemTone } from "$lib/requests/acquisition-list-item";
  import type { DownloadManagerEntry, DownloadTreeNode } from "./download-tree";

  let {
    node,
    entries,
    href,
    onSelectItem,
  }: {
    node: DownloadTreeNode;
    entries: DownloadManagerEntry[];
    href?: string;
    onSelectItem: (key: string) => void;
  } = $props();

  const progressValues = $derived(
    entries.map((entry) => entry.item.progress).filter((value): value is number => value !== null),
  );
  const progress = $derived(
    progressValues.length > 0
      ? progressValues.reduce((sum, value) => sum + value, 0) / progressValues.length
      : null,
  );
  const totalSize = $derived(
    entries.reduce((sum, entry) => sum + (numberValue(entry.row.totalSizeBytes) ?? 0), 0),
  );
  const totalSpeed = $derived(
    entries.reduce((sum, entry) => sum + (numberValue(entry.row.downloadSpeedBytesPerSecond) ?? 0), 0),
  );
  const longestEta = $derived(
    entries.reduce((longest, entry) => Math.max(longest, numberValue(entry.row.etaSeconds) ?? 0), 0),
  );
  const thumbnail = $derived(
    node.thumbnail
      ? { ...entityCardToThumbnailCard(node.thumbnail), aspectRatio: "square" as const }
      : null,
  );
  const entriesById = $derived(new Map(entries.map((entry) => [entry.item.id, entry])));
  const childRows = $derived(node.children.map((child) => {
    const childEntries = child.descendantEntryIds
      .map((id) => entriesById.get(id))
      .filter((entry): entry is DownloadManagerEntry => entry !== undefined);
    const isGroup = child.children.length > 0 || child.directEntryIds.length > 1;
    return {
      key: isGroup ? child.key : child.directEntryIds[0] ?? child.key,
      title: child.title,
      subtitle: child.thumbnail ? displayNameForEntityKind(child.thumbnail.kind) : "Entity",
      entries: childEntries,
    };
  }));
  const directRows = $derived(node.directEntryIds
    .map((id) => entriesById.get(id))
    .filter((entry): entry is DownloadManagerEntry => entry !== undefined)
    .map((entry) => ({
      key: entry.item.id,
      title: entry.item.title,
      subtitle: entry.item.subtitle ?? "Transfer",
      entries: [entry],
    })));
  const containedRows = $derived([...childRows, ...directRows]);

  function averageProgress(rowEntries: DownloadManagerEntry[]): number | null {
    const values = rowEntries
      .map((entry) => entry.item.progress)
      .filter((value): value is number => value !== null);
    return values.length > 0 ? values.reduce((sum, value) => sum + value, 0) / values.length : null;
  }

  function rowSpeed(rowEntries: DownloadManagerEntry[]): number {
    return rowEntries.reduce(
      (sum, entry) => sum + (numberValue(entry.row.downloadSpeedBytesPerSecond) ?? 0),
      0,
    );
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

  function rowTone(rowEntries: DownloadManagerEntry[]): AcquisitionItemTone {
    return rowEntries.reduce<AcquisitionItemTone>(
      (tone, entry) => tonePriority[entry.item.tone] > tonePriority[tone] ? entry.item.tone : tone,
      "done",
    );
  }

  function badgeVariant(tone: AcquisitionItemTone): BadgeVariant {
    if (tone === "failed") return "error";
    if (tone === "attention") return "warning";
    if (tone === "done") return "success";
    if (tone === "downloading" || tone === "searching") return "accent";
    return "default";
  }
</script>

<header class="group-header">
  <div class="group-identity">
    {#if thumbnail}
      <span class="group-artwork">
        <EntityThumbnail
          card={thumbnail}
          mediaOnly
          interactive={false}
          hoverPreviewsEnabled={false}
          showWantedBadge={false}
          artworkReactive
          imageFetchPriority="high"
        />
      </span>
    {/if}
    <span class="group-copy">
      <span class="group-kicker">Selected {node.thumbnail ? displayNameForEntityKind(node.thumbnail.kind) : "group"}</span>
      <strong>{node.title}</strong>
      <span>{entries.length} {entries.length === 1 ? "transfer" : "transfers"} across this Entity</span>
    </span>
    <Badge variant="accent">{entries.length} active</Badge>
  </div>
  {#if href}
    <Button variant="secondary" size="sm" onclick={() => window.location.assign(href)}>
      <ExternalLink class="h-3.5 w-3.5" /> Open entity
    </Button>
  {/if}
</header>

<div class="group-detail">
  <div class="group-metrics" aria-label={`${node.title} transfer summary`}>
    <span><small>Transfers</small><strong>{entries.length}</strong></span>
    <span><small>Progress</small><strong>{progress === null ? "—" : `${Math.round(progress * 100)}%`}</strong></span>
    <span><small>Total size</small><strong>{formatBytes(totalSize)}</strong></span>
    <span><small>Speed</small><strong>{formatSpeed(totalSpeed)}</strong></span>
    <span><small>Longest ETA</small><strong>{formatEta(longestEta)}</strong></span>
  </div>

  <section class="contained-transfers" aria-label={`${node.title} contained transfers`}>
    <header class="contained-header">
      <span><Layers3 class="h-3.5 w-3.5" /> Contained transfers</span>
      <small>Select a child Entity or transfer to drill into its details.</small>
    </header>
    <div class="transfer-list">
      {#each containedRows as row (row.key)}
        {@const rowProgress = averageProgress(row.entries)}
        {@const tone = rowTone(row.entries)}
        {@const singleEntry = row.entries.length === 1 ? row.entries[0] : null}
        <Button
          variant="ghost"
          class="group-transfer-row"
          aria-label={`Inspect ${row.title}`}
          onclick={() => onSelectItem(row.key)}
        >
          <span class="transfer-identity">
            <strong>{row.title}</strong>
            <small>{row.subtitle} · {row.entries.length} {row.entries.length === 1 ? "transfer" : "transfers"}</small>
          </span>
          <span class="transfer-progress">
            <span class="mini-track" aria-hidden="true">
              <span style:width={`${Math.round((rowProgress ?? 0) * 100)}%`}></span>
            </span>
            <small>{rowProgress === null ? "—" : `${Math.round(rowProgress * 100)}%`}</small>
          </span>
          <Badge variant={badgeVariant(tone)}>{singleEntry?.item.statusLabel ?? `${row.entries.length} transfers`}</Badge>
          <span class="transfer-speed">{formatSpeed(rowSpeed(row.entries))}</span>
          <ChevronRight class="h-3.5 w-3.5 text-text-disabled" />
        </Button>
      {/each}
    </div>
  </section>
</div>

<style>
  .group-header { display: flex; min-height: 4.8rem; align-items: center; justify-content: space-between; gap: 1rem; padding: 0.7rem 0.9rem; border-bottom: 1px solid var(--color-border-subtle); background: rgb(255 255 255 / 0.012); }
  .group-identity { display: flex; min-width: 0; align-items: center; gap: 0.7rem; }
  .group-artwork { flex: 0 0 3.2rem; width: 3.2rem; overflow: hidden; border-radius: var(--radius-xs, 4px); box-shadow: 0 0 0 1px var(--color-border-default), 0 7px 18px rgb(0 0 0 / 0.32); }
  .group-artwork :global(.media) { border: 0; border-radius: var(--radius-xs, 4px); box-shadow: none; }
  .group-copy { display: flex; min-width: 0; flex-direction: column; gap: 0.08rem; }
  .group-copy strong { overflow: hidden; color: var(--color-text-primary); font-family: var(--font-heading, "Geist", sans-serif); font-size: 1rem; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
  .group-copy > span:last-child { color: var(--color-text-muted); font-size: 0.7rem; }
  .group-kicker { color: var(--color-text-muted); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.58rem; letter-spacing: 0.1em; text-transform: uppercase; }
  .group-detail { min-width: 0; min-height: 0; flex: 1 1 auto; overflow: auto; padding: 0.8rem; }
  .group-metrics { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); border: 1px solid var(--color-border-subtle); background: rgb(255 255 255 / 0.012); }
  .group-metrics > span { display: flex; min-width: 0; flex-direction: column; gap: 0.16rem; padding: 0.58rem 0.7rem; border-right: 1px solid var(--color-border-subtle); }
  .group-metrics > span:last-child { border-right: 0; }
  .group-metrics small, .contained-header small, .transfer-identity small, .transfer-progress small { color: var(--color-text-muted); font-size: 0.62rem; }
  .group-metrics strong { overflow: hidden; color: var(--color-text-secondary); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.74rem; font-weight: 500; text-overflow: ellipsis; white-space: nowrap; }
  .contained-transfers { margin-top: 0.7rem; border: 1px solid var(--color-border-subtle); }
  .contained-header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 0.5rem 0.65rem; border-bottom: 1px solid var(--color-border-subtle); background: rgb(255 255 255 / 0.012); }
  .contained-header > span { display: flex; align-items: center; gap: 0.4rem; color: var(--color-text-secondary); font-family: var(--font-heading, "Geist", sans-serif); font-size: 0.72rem; font-weight: 600; }
  .transfer-list { display: flex; flex-direction: column; }
  :global(.group-transfer-row) { display: grid; width: 100%; min-height: 2.75rem; grid-template-columns: minmax(12rem, 1fr) minmax(8rem, 0.45fr) minmax(7rem, auto) 6rem 1rem; align-items: center; gap: 0.7rem; padding: 0.42rem 0.65rem; border-bottom: 1px solid var(--color-border-subtle); border-radius: 0; text-align: left; }
  :global(.group-transfer-row:last-child) { border-bottom: 0; }
  .transfer-identity { display: flex; min-width: 0; flex-direction: column; gap: 0.08rem; }
  .transfer-identity strong { overflow: hidden; color: var(--color-text-primary); font-size: 0.74rem; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
  .transfer-progress { display: flex; min-width: 0; align-items: center; gap: 0.45rem; }
  .mini-track { height: 0.25rem; flex: 1 1 auto; overflow: hidden; background: var(--color-surface-4); }
  .mini-track > span { display: block; height: 100%; background: var(--color-accent-400); }
  .transfer-speed { color: var(--color-text-muted); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.65rem; text-align: right; }

  @media (max-width: 760px) {
    .group-header { align-items: flex-start; flex-direction: column; }
    .group-metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .group-metrics > span { border-bottom: 1px solid var(--color-border-subtle); }
    :global(.group-transfer-row) { grid-template-columns: minmax(9rem, 1fr) auto 1rem; }
    .transfer-progress, .transfer-speed { display: none; }
    .contained-header small { display: none; }
  }
</style>
