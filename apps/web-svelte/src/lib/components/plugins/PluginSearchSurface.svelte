<script lang="ts">
  import { Loader2, RefreshCw, ScanSearch, Search } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import type { PluginSearchField } from "$lib/api/generated/model";
  import type { EntitySearchCandidate, PluginProvider } from "$lib/api/identify-types";
  import IdentifyProviderSelect from "$lib/components/identify/IdentifyProviderSelect.svelte";
  import PluginCandidateList from "$lib/components/review/PluginCandidateList.svelte";
  import PluginSearchForm from "./PluginSearchForm.svelte";

  interface Props {
    providers: PluginProvider[];
    selectedProviderId: string;
    fields: PluginSearchField[];
    values: Record<string, string>;
    onProviderChange: (providerId: string) => void;
    onValuesChange: (values: Record<string, string>) => void;
    onSubmit: () => void;
    onClear: () => void;
    title?: string;
    description?: string;
    providerLabel?: string;
    searching?: boolean;
    disabled?: boolean;
    submitDisabled?: boolean;
    candidates?: EntitySearchCandidate[];
    entityKind?: string;
    hasSearched?: boolean;
    onActivate?: (candidate: EntitySearchCandidate, candidateKey: string) => void;
    onPreview?: (candidate: EntitySearchCandidate, candidateKey: string) => void;
    activeCandidateKey?: string | null;
    candidateDisabled?: boolean;
    candidateStatus?: string | null;
    searchStatus?: string | null;
    onLoadMore?: (() => void) | null;
    loadingMore?: boolean;
    onSeek?: (() => void) | null;
    seeking?: boolean;
    seekDisabled?: boolean;
    onRescan?: (() => void) | null;
    rescanning?: boolean;
    noProvidersMessage?: string;
  }

  let {
    providers,
    selectedProviderId,
    fields,
    values,
    onProviderChange,
    onValuesChange,
    onSubmit,
    onClear,
    title = "Search",
    description = "select a provider and query",
    providerLabel = "Provider",
    searching = false,
    disabled = false,
    submitDisabled = false,
    candidates,
    entityKind,
    hasSearched = false,
    onActivate,
    onPreview,
    activeCandidateKey = null,
    candidateDisabled = false,
    candidateStatus = null,
    searchStatus = null,
    onLoadMore = null,
    loadingMore = false,
    onSeek = null,
    seeking = false,
    seekDisabled = false,
    onRescan = null,
    rescanning = false,
    noProvidersMessage = "No enabled provider supports this content kind.",
  }: Props = $props();

  const activeProvider = $derived(
    providers.find((provider) => provider.id === selectedProviderId) ?? providers[0] ?? null,
  );
  const showCandidates = $derived(
    providers.length > 0 && candidates !== undefined && Boolean(entityKind) && Boolean(onActivate),
  );
</script>

<div class="space-y-4">
  <section class="surface-panel relative z-20 overflow-visible">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <Search class="h-3.5 w-3.5 text-text-accent" />
      <span class="text-kicker text-text-accent">{title}</span>
      <span class="hidden font-mono text-[0.7rem] text-text-muted sm:inline">{description}</span>
      <div class="flex-1"></div>
      {#if onRescan}
        <Button
          type="button"
          size="sm"
          variant="secondary"
          disabled={disabled || searching || rescanning}
          class="gap-1.5"
          onclick={onRescan}
        >
          <RefreshCw class={cn("h-3 w-3", rescanning && "animate-spin")} />
          Rescan
        </Button>
      {/if}
    </header>

    <div class="flex flex-col gap-3 p-3.5">
      {#if providers.length > 0}
        <div class="grid min-w-0 grid-cols-1 gap-2 sm:grid-cols-[minmax(12rem,32rem)_auto] sm:items-end">
          <div class="plugin-search-provider flex min-w-0 flex-col gap-1.5">
            <span class="font-mono text-[0.72rem] text-text-muted">{providerLabel}</span>
            <IdentifyProviderSelect
              {providers}
              selectedId={activeProvider?.id ?? selectedProviderId}
              onChange={onProviderChange}
              label={providerLabel}
              compact
            />
          </div>
          {#if onSeek}
            <Button
              type="button"
              variant="secondary"
              disabled={disabled || seekDisabled}
              class="w-full gap-1.5 sm:w-24"
              onclick={onSeek}
            >
              {#if seeking}
                <Loader2 class="h-3.5 w-3.5 animate-spin" />
              {:else}
                <ScanSearch class="h-3.5 w-3.5" />
              {/if}
              Seek
            </Button>
          {/if}
        </div>

        <PluginSearchForm
          {fields}
          {values}
          {onValuesChange}
          {onSubmit}
          {onClear}
          loading={searching}
          {disabled}
          {submitDisabled}
        />
      {:else}
        <div class="rounded-xs border border-warning/30 bg-warning-muted px-3 py-2.5 text-[0.82rem] text-warning-text">
          {noProvidersMessage}
        </div>
      {/if}
      {#if searchStatus}
        <div class="flex items-center gap-2 rounded-xs border border-border-subtle bg-surface-1 px-3 py-2 font-mono text-[0.72rem] text-text-muted">
          <Loader2 class="h-3.5 w-3.5 animate-spin text-text-accent" />
          <span>{searchStatus}</span>
        </div>
      {/if}
    </div>
  </section>

  {#if showCandidates}
    <section class="surface-panel relative z-0 overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <span class="text-kicker text-text-accent">Candidates</span>
        {#if hasSearched || (candidates?.length ?? 0) > 0}
          <span class="font-mono text-[0.7rem] text-text-muted">{candidates?.length ?? 0} found</span>
        {/if}
        {#if candidateStatus}
          <span class="hidden font-mono text-[0.7rem] text-text-muted md:inline">{candidateStatus}</span>
        {/if}
      </header>
      <div class="p-3.5">
        {#if (candidates?.length ?? 0) > 0 && onActivate && entityKind}
          <PluginCandidateList
            candidates={candidates ?? []}
            {entityKind}
            {activeCandidateKey}
            disabled={candidateDisabled || searching}
            {onActivate}
            {onPreview}
          />
          {#if onLoadMore}
            <div class="mt-3 flex justify-center border-t border-border-subtle pt-3">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                class="gap-1.5"
                disabled={candidateDisabled || searching || loadingMore}
                onclick={onLoadMore}
              >
                {#if loadingMore}<Loader2 class="h-3.5 w-3.5 animate-spin" />{/if}
                {loadingMore ? "Loading more…" : "Load more"}
              </Button>
            </div>
          {/if}
        {:else if searching}
          <div class="flex items-center justify-center gap-2.5 p-7 text-text-muted" role="status">
            <Loader2 class="h-4 w-4 animate-spin" />
            <span class="text-sm">Searching {activeProvider?.name ?? "provider"}…</span>
          </div>
        {:else if hasSearched}
          <div class="empty-rack-slot p-6 text-center">
            <p class="text-sm text-text-muted">No usable candidates found. Try a different search.</p>
          </div>
        {:else}
          <div class="empty-rack-slot p-6 text-center">
            <p class="text-sm text-text-muted">Enter the provider-specific details above to find candidates.</p>
          </div>
        {/if}
      </div>
    </section>
  {/if}
</div>

<style>
  .plugin-search-provider :global(.provider-select) {
    width: 100%;
  }
</style>
