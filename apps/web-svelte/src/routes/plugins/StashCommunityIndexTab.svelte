<script lang="ts">
  import { Check, Download, Loader2, RefreshCw, Search, X } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import type { CommunityIndexEntry } from "$lib/api/plugins";

  interface Props {
    entries: CommunityIndexEntry[];
    installingId: string | null;
    loaded: boolean;
    loading: boolean;
    onInstall: (id: string) => void;
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
          entry.name.toLowerCase().includes(q) || entry.id.toLowerCase().includes(q),
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
      <div class="relative">
        <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-text-disabled" />
        <input
          class="control-input pl-8 w-64 py-1.5 text-sm"
          placeholder="Filter by name or ID..."
          bind:value={search}
        />
        {#if search}
          <button
            onclick={() => (search = "")}
            aria-label="Clear search"
            class="absolute right-2 top-1/2 -translate-y-1/2 text-text-disabled hover:text-text-muted"
          >
            <X class="h-3 w-3" />
          </button>
        {/if}
      </div>
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
      {#each filteredEntries as entry (entry.id)}
        <div class="surface-card no-lift px-4 py-3 flex items-center gap-3">
          <div class="min-w-0 flex-1">
            <p class="text-sm font-medium">{entry.name}</p>
            <p class="text-text-disabled text-[0.65rem] mt-0.5 font-mono">
              {entry.id}
              <span class="text-text-disabled/60 ml-2">{entry.date}</span>
              {#if entry.requires?.length}
                <span class="text-text-disabled/60 ml-2">requires: {entry.requires.join(", ")}</span>
              {/if}
            </p>
          </div>
          {#if entry.installed}
            <Badge variant="accent">
              <Check class="h-2.5 w-2.5 mr-1" />Installed
            </Badge>
          {:else}
            <button
              onclick={() => onInstall(entry.id)}
              disabled={installingId === entry.id}
              class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted hover:text-text-accent transition-colors duration-fast shrink-0 disabled:opacity-40"
            >
              {#if installingId === entry.id}
                <Loader2 class="h-3.5 w-3.5 animate-spin" />
              {:else}
                <Download class="h-3.5 w-3.5" />
              {/if}
              Install
            </button>
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
