<script lang="ts">
  import { ArrowDown, ArrowUp, BookOpen, ChevronsUpDown, Download, ExternalLink, FileText, Film, Headphones, Tag } from "@lucide/svelte";
  import { Button, Select, cn } from "@prismedia/ui-svelte";
  import type { Component } from "svelte";
  import type { ReleaseCandidateView } from "$lib/api/generated/model";

  interface Props {
    candidates: ReleaseCandidateView[];
    /** Whether the acquisition is still awaiting a selection (enables the Download action). */
    canChoose: boolean;
    busy: boolean;
    onQueue: (candidate: ReleaseCandidateView) => void;
  }

  let { candidates, canChoose, busy, onQueue }: Props = $props();

  type SortKey = "title" | "indexer" | "size" | "seeders" | "score";

  // Prowlarr embeds the category as a " » Other/Audio" breadcrumb at the end of the title (often after a
  // newline). Split it off so the title is clean and the category can render as its own type indicator.
  function splitCategory(rawTitle: string): { title: string; category: string | null } {
    const marker = rawTitle.lastIndexOf("»");
    if (marker === -1) {
      return { title: rawTitle.replace(/\s+/g, " ").trim(), category: null };
    }
    const title = rawTitle.slice(0, marker).replace(/\s+/g, " ").trim();
    const category = rawTitle.slice(marker + 1).replace(/\s+/g, " ").trim();
    return { title: title || rawTitle.trim(), category: category || null };
  }

  /** Maps a Prowlarr category breadcrumb to a type icon. */
  function categoryIcon(category: string | null): Component {
    const value = (category ?? "").toLowerCase();
    if (/audio|music|mp3|flac|audiobook|m4b/.test(value)) return Headphones;
    if (/book|ebook|comic|magazine/.test(value)) return BookOpen;
    if (/doc|pdf|epub|text/.test(value)) return FileText;
    if (/video|movie|tv|film/.test(value)) return Film;
    return Tag;
  }

  function formatBytes(bytes: number): string {
    if (!bytes || bytes <= 0) return "—";
    const mb = bytes / 1_000_000;
    return mb >= 1000 ? `${(mb / 1000).toFixed(2)} GB` : `${mb.toFixed(1)} MB`;
  }

  let sortKey = $state<SortKey>("score");
  let sortDir = $state<"asc" | "desc">("desc");

  const rows = $derived(
    candidates.map((candidate) => ({ candidate, ...splitCategory(candidate.title) })),
  );

  const sorted = $derived(
    [...rows].sort((a, b) => {
      const direction = sortDir === "asc" ? 1 : -1;
      switch (sortKey) {
        case "title":
          return a.title.localeCompare(b.title) * direction;
        case "indexer":
          return a.candidate.indexerName.localeCompare(b.candidate.indexerName) * direction;
        case "size":
          return (Number(a.candidate.sizeBytes) - Number(b.candidate.sizeBytes)) * direction;
        case "seeders":
          return (Number(a.candidate.seeders ?? 0) - Number(b.candidate.seeders ?? 0)) * direction;
        case "score":
          return (Number(a.candidate.score) - Number(b.candidate.score)) * direction;
      }
    }),
  );

  // Text keys default to A→Z; numeric keys default to high→low (best first).
  function toggleSort(key: SortKey) {
    if (sortKey === key) {
      sortDir = sortDir === "asc" ? "desc" : "asc";
    } else {
      sortKey = key;
      sortDir = key === "title" || key === "indexer" ? "asc" : "desc";
    }
  }

  const columns: { key: SortKey; label: string; align: "left" | "right" }[] = [
    { key: "title", label: "Release", align: "left" },
    { key: "indexer", label: "Indexer", align: "left" },
    { key: "size", label: "Size", align: "right" },
    { key: "seeders", label: "Seeders", align: "right" },
    { key: "score", label: "Score", align: "right" },
  ];

  // Mobile sort control: headers are hidden on narrow screens, so expose the same sorts as a select.
  const mobileSortOptions = [
    { value: "score:desc", label: "Best match" },
    { value: "seeders:desc", label: "Most seeders" },
    { value: "size:desc", label: "Largest" },
    { value: "size:asc", label: "Smallest" },
    { value: "title:asc", label: "Title A–Z" },
  ];
  const mobileSortValue = $derived(`${sortKey}:${sortDir}`);
  function setMobileSort(value: string) {
    const [key, dir] = value.split(":");
    sortKey = key as SortKey;
    sortDir = dir === "asc" ? "asc" : "desc";
  }

  function rejectionText(candidate: ReleaseCandidateView): string {
    return candidate.rejections.map((reason) => String(reason).replace(/-/g, " ")).join(", ");
  }
</script>

<!-- Mobile sort toggle (column headers are hidden on cards) -->
<label class="flex items-center justify-end gap-2 sm:hidden">
  <span class="text-label text-text-muted">Sort</span>
  <Select size="sm" value={mobileSortValue} options={mobileSortOptions} onchange={setMobileSort} />
</label>

<!-- ── Desktop: sortable table ── -->
<div class="hidden overflow-x-auto rounded-sm border border-border-subtle sm:block">
  <table class="w-full text-sm">
    <thead class="bg-surface-1 text-left text-[0.7rem] uppercase tracking-wide text-text-muted">
      <tr>
        <th class="w-8 px-3 py-2"><span class="sr-only">Type</span></th>
        {#each columns as col (col.key)}
          <th class={cn("px-3 py-2", col.align === "right" && "text-right")}>
            <button
              type="button"
              onclick={() => toggleSort(col.key)}
              class={cn(
                "inline-flex items-center gap-1 uppercase tracking-wide transition-colors hover:text-text-primary",
                sortKey === col.key ? "text-text-accent" : "text-text-muted",
                col.align === "right" && "flex-row-reverse",
              )}
              aria-label={`Sort by ${col.label}`}
            >
              {col.label}
              {#if sortKey === col.key}
                {#if sortDir === "asc"}<ArrowUp class="h-3 w-3" />{:else}<ArrowDown class="h-3 w-3" />{/if}
              {:else}
                <ChevronsUpDown class="h-3 w-3 opacity-40" />
              {/if}
            </button>
          </th>
        {/each}
        <th class="px-3 py-2"><span class="sr-only">Actions</span></th>
      </tr>
    </thead>
    <tbody>
      {#each sorted as row (row.candidate.id)}
        {@const c = row.candidate}
        {@const CatIcon = categoryIcon(row.category)}
        <tr class={cn("border-t border-border-subtle", !c.accepted && "opacity-55")}>
          <td class="px-3 py-2 align-middle">
            <span class="inline-flex text-text-muted" title={row.category ?? "Unknown type"} aria-label={row.category ?? "Unknown type"}>
              <CatIcon class="h-4 w-4" />
            </span>
          </td>
          <td class="px-3 py-2">
            <div class="truncate text-text-primary" title={row.title}>{row.title}</div>
            {#if !c.accepted && c.rejections.length > 0}
              <div class="text-[0.7rem] text-warning-text">{rejectionText(c)}</div>
            {/if}
          </td>
          <td class="px-3 py-2 text-text-muted">{c.indexerName}</td>
          <td class="px-3 py-2 text-right text-text-muted">{formatBytes(Number(c.sizeBytes))}</td>
          <td class="px-3 py-2 text-right text-text-muted">{c.seeders ?? "—"}</td>
          <td class="px-3 py-2 text-right font-mono text-[0.72rem] text-text-muted">{Number(c.score).toFixed(0)}</td>
          <td class="px-3 py-2">
            <div class="flex items-center justify-end gap-1.5">
              {#if c.infoUrl}
                <a href={c.infoUrl} target="_blank" rel="noopener" title="Open release page" class="inline-flex items-center text-text-muted transition-colors hover:text-text-accent">
                  <ExternalLink class="h-3.5 w-3.5" />
                </a>
              {/if}
              {#if c.accepted && canChoose}
                <Button size="sm" onclick={() => onQueue(c)} disabled={busy} class="gap-1.5">
                  <Download class="h-3.5 w-3.5" />
                  Download
                </Button>
              {/if}
            </div>
          </td>
        </tr>
      {/each}
    </tbody>
  </table>
</div>

<!-- ── Mobile: stacked cards (type+title, info, actions) ── -->
<div class="space-y-2 sm:hidden">
  {#each sorted as row (row.candidate.id)}
    {@const c = row.candidate}
    {@const CatIcon = categoryIcon(row.category)}
    <div class={cn("rounded-sm border border-border-subtle bg-surface-1 p-3", !c.accepted && "opacity-55")}>
      <div class="flex items-start gap-2">
        <span class="mt-0.5 inline-flex shrink-0 text-text-muted" title={row.category ?? "Unknown type"} aria-label={row.category ?? "Unknown type"}>
          <CatIcon class="h-4 w-4" />
        </span>
        <div class="min-w-0 flex-1">
          <div class="text-sm text-text-primary">{row.title}</div>
          {#if row.category}<div class="mt-0.5 font-mono text-[0.62rem] text-text-muted">{row.category}</div>{/if}
          {#if !c.accepted && c.rejections.length > 0}
            <div class="mt-0.5 text-[0.7rem] text-warning-text">{rejectionText(c)}</div>
          {/if}
        </div>
      </div>
      <div class="mt-2 flex flex-wrap gap-x-4 gap-y-1 font-mono text-[0.7rem] text-text-muted">
        <span>{c.indexerName}</span>
        <span>{formatBytes(Number(c.sizeBytes))}</span>
        <span>{c.seeders ?? "—"} seeders</span>
        <span>score {Number(c.score).toFixed(0)}</span>
      </div>
      {#if c.infoUrl || (c.accepted && canChoose)}
        <div class="mt-2.5 flex items-center justify-end gap-2">
          {#if c.infoUrl}
            <a href={c.infoUrl} target="_blank" rel="noopener" class="inline-flex items-center gap-1.5 text-[0.75rem] text-text-muted transition-colors hover:text-text-accent">
              <ExternalLink class="h-3.5 w-3.5" />
              Release page
            </a>
          {/if}
          {#if c.accepted && canChoose}
            <Button size="sm" onclick={() => onQueue(c)} disabled={busy} class="gap-1.5">
              <Download class="h-3.5 w-3.5" />
              Download
            </Button>
          {/if}
        </div>
      {/if}
    </div>
  {/each}
</div>
