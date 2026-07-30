<script lang="ts">
  import { Check, Download, Loader2, RefreshCw } from "@lucide/svelte";
  import { Badge, Button, SearchInput } from "@prismedia/ui-svelte";

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

<section class="space-y-3">
  <div class="flex items-center justify-between gap-3 flex-wrap">
    <p class="text-text-muted text-[0.72rem]">
      {entries.length} scrapers available · All Stash community scrapers are classified as NSFW
    </p>
    <div class="flex items-center gap-2">
      <SearchInput
        class="w-64"
        inputClass="text-sm"
        ariaLabel="Filter scrapers by name or ID"
        placeholder="Filter by name or ID..."
        bind:value={search}
      />
      <Button variant="secondary" size="sm" onclick={onRefresh} disabled={loading}>
        {#if loading}
          <Loader2 class="h-3.5 w-3.5 animate-spin" />
        {:else}
          <RefreshCw class="h-3.5 w-3.5" />
        {/if}
        Refresh
      </Button>
    </div>
  </div>

  {#if loading && !loaded}
    <div class="surface-card no-lift p-12 flex items-center justify-center">
      <Loader2 class="h-6 w-6 animate-spin text-text-muted" />
    </div>
  {:else}
    <div class="space-y-1 max-h-[600px] overflow-y-auto scrollbar-hidden">
      {#each filteredEntries as entry (entry.providerId)}
        <div class="surface-card no-lift px-4 py-3 flex items-center gap-3">
          <div class="min-w-0 flex-1">
            <p class="text-sm font-medium">{entry.name}</p>
            <p class="text-text-disabled text-[0.65rem] mt-0.5 font-mono">
              {entry.providerId}
              <span class="text-text-disabled/60 ml-2">{entry.version}</span>
            </p>
          </div>
          {#if entry.installed}
            <Badge variant="accent">
              <Check class="h-2.5 w-2.5 mr-1" />Installed
            </Badge>
          {:else}
            <Button
              variant="ghost"
              size="sm"
              onclick={() => onInstall(entry.providerId)}
              disabled={installingId === entry.providerId}
              class="shrink-0 text-text-muted hover:text-text-accent"
            >
              {#if installingId === entry.providerId}
                <Loader2 class="h-3.5 w-3.5 animate-spin" />
              {:else}
                <Download class="h-3.5 w-3.5" />
              {/if}
              Install
            </Button>
          {/if}
        </div>
      {/each}
      {#if filteredEntries.length === 0}
        <div class="surface-card no-lift p-8 text-center">
          <p class="text-text-muted text-sm">
            {search ? "No scrapers match your search." : "Index is empty."}
          </p>
        </div>
      {/if}
    </div>
  {/if}
</section>
