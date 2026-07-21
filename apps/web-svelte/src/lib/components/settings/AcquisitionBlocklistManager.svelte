<script lang="ts">
  import { ChevronDown, ChevronRight, ListFilter, Trash2 } from "@lucide/svelte";
  import { Badge, Button, SearchInput } from "@prismedia/ui-svelte";
  import type { AcquisitionBlocklistEntry, AcquisitionHistoryView } from "$lib/api/generated/model";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import { formatRelativeTime } from "$lib/utils/format";
  import {
    blocklistGroupMatches,
    groupAcquisitionBlocklist,
    type AcquisitionBlocklistGroup,
  } from "$lib/requests/acquisition-blocklist";

  const entryBatchSize = 25;
  const groupBatchSize = 20;

  let {
    entries,
    history,
    reasonLabels,
    busy = false,
    onRemove,
  }: {
    entries: AcquisitionBlocklistEntry[];
    history: AcquisitionHistoryView[];
    reasonLabels: Partial<Record<string, string>>;
    busy?: boolean;
    onRemove: (id: string) => void | Promise<void>;
  } = $props();

  let query = $state("");
  let expandedKeys = $state<string[]>([]);
  let visibleGroupCount = $state(groupBatchSize);
  let visibleEntryCounts = $state<Record<string, number>>({});

  const groups = $derived(groupAcquisitionBlocklist(entries, history));
  const matchingGroups = $derived(groups.filter((group) => blocklistGroupMatches(group, query)));
  const visibleGroups = $derived(
    query.trim() ? matchingGroups : matchingGroups.slice(0, visibleGroupCount),
  );
  const hiddenGroupCount = $derived(Math.max(0, matchingGroups.length - visibleGroups.length));

  function isExpanded(group: AcquisitionBlocklistGroup): boolean {
    return query.trim().length > 0 || expandedKeys.includes(group.key);
  }

  function toggleGroup(key: string) {
    expandedKeys = expandedKeys.includes(key)
      ? expandedKeys.filter((candidate) => candidate !== key)
      : [...expandedKeys, key];
  }

  function visibleEntries(group: AcquisitionBlocklistGroup): AcquisitionBlocklistEntry[] {
    return group.entries.slice(0, visibleEntryCounts[group.key] ?? entryBatchSize);
  }

  function showMoreEntries(group: AcquisitionBlocklistGroup) {
    visibleEntryCounts = {
      ...visibleEntryCounts,
      [group.key]: (visibleEntryCounts[group.key] ?? entryBatchSize) + entryBatchSize,
    };
  }
</script>

<section class="space-y-3">
  <div class="space-y-0.5">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h3 class="text-kicker text-text-primary">Blocklist</h3>
      {#if entries.length > 0}
        <span class="font-mono text-[0.68rem] text-text-muted">
          {entries.length} {entries.length === 1 ? "release" : "releases"} · {groups.length} {groups.length === 1 ? "work" : "works"}
        </span>
      {/if}
    </div>
    <p class="text-[0.72rem] text-text-muted">
      Releases Prismedia will not grab again. Search by work, release, indexer, hash, or failure detail; expand only the work you need.
    </p>
  </div>

  {#if entries.length > 0}
    <SearchInput
      bind:value={query}
      ariaLabel="Search blocklisted releases"
      placeholder="Search works, releases, indexers, or errors…"
    />
  {/if}

  {#if entries.length === 0}
    <p class="text-[0.78rem] text-text-muted">No blocklisted releases.</p>
  {:else if matchingGroups.length === 0}
    <div class="empty-rack-slot flex flex-col items-center gap-2 p-6 text-center">
      <ListFilter class="h-5 w-5 text-text-muted" />
      <p class="text-sm text-text-muted">No blocklist entries match “{query.trim()}”.</p>
    </div>
  {:else}
    <div class="space-y-2">
      {#each visibleGroups as group (group.key)}
        <section class="overflow-hidden rounded-sm border border-border-subtle bg-surface-1">
          <Button
            type="button"
            variant="ghost"
            class="no-lift flex h-auto w-full justify-start rounded-none px-3 py-2 text-left"
            aria-expanded={isExpanded(group)}
            onclick={() => toggleGroup(group.key)}
          >
            {#if isExpanded(group)}
              <ChevronDown class="h-3.5 w-3.5 shrink-0 text-text-muted" />
            {:else}
              <ChevronRight class="h-3.5 w-3.5 shrink-0 text-text-muted" />
            {/if}
            <span class="min-w-0 flex-1">
              <span class="flex flex-wrap items-center gap-1.5">
                <span class="truncate text-sm font-medium text-text-primary">{group.title}</span>
                {#if group.kind}<Badge variant="default">{labelForEntityKind(group.kind)}</Badge>{/if}
              </span>
              <span class="mt-0.5 block text-[0.68rem] text-text-muted">
                {group.entries.length} blocked {group.entries.length === 1 ? "release" : "releases"} · latest {formatRelativeTime(group.newestAt)}
              </span>
            </span>
          </Button>

          {#if isExpanded(group)}
            <div class="divide-y divide-border-subtle border-t border-border-subtle">
              {#each visibleEntries(group) as entry (entry.id)}
                <div class="flex items-start justify-between gap-2 px-3 py-2.5">
                  <div class="min-w-0 flex-1 space-y-1">
                    <div class="flex min-w-0 flex-wrap items-center gap-1.5">
                      <Badge variant="default">{reasonLabels[entry.reason] ?? entry.reason}</Badge>
                      {#if entry.indexerName}<span class="text-xs text-text-muted">{entry.indexerName}</span>{/if}
                      <span class="min-w-0 basis-full break-words text-sm text-text-primary sm:basis-auto sm:flex-1" title={entry.title ?? undefined}>
                        {entry.title ?? "Unknown release"}
                      </span>
                    </div>
                    {#if entry.message}<p class="break-words text-[0.7rem] text-text-muted">{entry.message}</p>{/if}
                    <p class="font-mono text-[0.64rem] text-text-disabled">Blocked {formatRelativeTime(entry.createdAt)}</p>
                  </div>
                  <Button
                    size="sm"
                    variant="ghost"
                    class="no-lift shrink-0 gap-1.5"
                    onclick={() => void onRemove(entry.id)}
                    disabled={busy}
                    aria-label={`Allow ${entry.title ?? "release"} again`}
                    title="Remove from blocklist"
                  >
                    <Trash2 class="h-3.5 w-3.5" />
                    <span class="hidden sm:inline">Allow again</span>
                  </Button>
                </div>
              {/each}
              {#if visibleEntries(group).length < group.entries.length}
                <div class="flex justify-center px-3 py-2">
                  <Button size="sm" variant="ghost" onclick={() => showMoreEntries(group)}>
                    Show {Math.min(entryBatchSize, group.entries.length - visibleEntries(group).length)} more
                  </Button>
                </div>
              {/if}
            </div>
          {/if}
        </section>
      {/each}
    </div>

    {#if hiddenGroupCount > 0}
      <div class="flex justify-center">
        <Button size="sm" variant="secondary" onclick={() => (visibleGroupCount += groupBatchSize)}>
          Show {Math.min(groupBatchSize, hiddenGroupCount)} more works
        </Button>
      </div>
    {/if}
  {/if}
</section>
