<script lang="ts">
  import { Check, Download, Globe, Loader2, RefreshCw } from "@lucide/svelte";
  import { Button, SearchInput } from "@prismedia/ui-svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";

  /** A Stash community scraper row with its locally-installed state resolved. */
  export interface StashScraperRow {
    providerId: string;
    name: string;
    version: string;
    installed: boolean;
  }

  interface Props {
    entries: StashScraperRow[];
    installingId: string | null;
    loaded: boolean;
    loading: boolean;
    onInstall: (providerId: string) => void;
    onRefresh: () => void;
  }

  let {
    entries,
    installingId,
    loaded,
    loading,
    onInstall,
    onRefresh,
  }: Props = $props();

  let search = $state("");

  const filteredEntries = $derived.by(() => {
    const q = search.trim().toLowerCase();
    return q
      ? entries.filter((entry) =>
          entry.name.toLowerCase().includes(q) || entry.providerId.toLowerCase().includes(q),
        )
      : entries;
  });
</script>

<section class="flex flex-col gap-4">
  <div class="flex flex-wrap items-center gap-2">
    <SearchInput
      class="w-full sm:w-64"
      inputClass="text-sm"
      ariaLabel="Filter scrapers by name or ID"
      placeholder="Filter by name or ID..."
      bind:value={search}
    />
    <Button variant="ghost" size="sm" onclick={onRefresh} disabled={loading}>
      {#if loading}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<RefreshCw aria-hidden="true" />{/if}
      Refresh
    </Button>
  </div>

  {#if loading && !loaded}
    <StatePlaceholder icon={Globe} title="Loading scraper index" busy />
  {:else if filteredEntries.length === 0}
    <StatePlaceholder icon={Globe} title={search ? "No matching scrapers" : "Index is empty"} />
  {:else}
    <ul
      class="flex max-h-[600px] flex-col divide-y divide-[var(--color-border-subtle)] overflow-y-auto rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] scrollbar-hidden"
    >
      {#each filteredEntries as entry (entry.providerId)}
        <li class="flex min-w-0 items-center gap-3 px-3 py-2.5 sm:px-4">
          <div class="min-w-0 flex-1">
            <p class="truncate text-label font-medium text-text-primary">{entry.name}</p>
            <p class="truncate font-mono text-[0.64rem] text-text-disabled">
              {entry.providerId}<span class="ml-2">{entry.version}</span>
            </p>
          </div>
          {#if entry.installed}
            <span class="flex h-control-sm shrink-0 items-center gap-1.5 px-2 text-caption text-text-muted">
              <Check class="size-3.5" aria-hidden="true" />
              Installed
            </span>
          {:else}
            <Button
              variant="secondary"
              size="sm"
              class="shrink-0"
              onclick={() => onInstall(entry.providerId)}
              disabled={installingId === entry.providerId}
            >
              {#if installingId === entry.providerId}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Download aria-hidden="true" />{/if}
              Install
            </Button>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</section>
