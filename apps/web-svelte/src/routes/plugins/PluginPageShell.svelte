<script lang="ts">
  import type { Snippet } from "svelte";
  import { Boxes, Globe, Loader2, Plug, Puzzle, Sparkles, X } from "@lucide/svelte";
  import { entityTerms } from "$lib/terminology";
  import type { PluginTabDefinition, PluginsTab } from "./plugin-page-types";

  let {
    isSfw,
    loading,
    error,
    message,
    tab,
    visibleTabs,
    installedCount,
    videoCount,
    performerCount,
    stashBoxCount,
    prismediaCount,
    onDismissError,
    onTabChange,
    children,
  }: {
    isSfw: boolean;
    loading: boolean;
    error: string | null;
    message: string | null;
    tab: PluginsTab;
    visibleTabs: PluginTabDefinition[];
    installedCount: number;
    videoCount: number;
    performerCount: number;
    stashBoxCount: number;
    prismediaCount: number;
    onDismissError: () => void;
    onTabChange: (tab: PluginsTab) => void;
    children: Snippet;
  } = $props();

  function tabIcon(key: PluginsTab) {
    if (key === "installed") return Boxes;
    if (key === "prismedia-index") return Sparkles;
    if (key === "stash-index") return Globe;
    return Plug;
  }
</script>

<div class="space-y-4">
  <div>
    <h1 class="flex items-center gap-2.5">
      <Puzzle class="h-5 w-5 text-text-accent" />
      Plugins
    </h1>
    <p class="mt-1 text-text-muted text-[0.78rem]">
      Install and manage identification plugins and metadata providers
    </p>
  </div>

  <div class="grid gap-2 {isSfw ? 'grid-cols-2' : 'grid-cols-4'}">
    <div class="surface-stat px-3 py-2">
      <span class="text-kicker !text-text-disabled">Installed</span>
      <div class="text-lg font-semibold text-text-primary leading-tight">
        {installedCount}
      </div>
    </div>
    {#if !isSfw}
      <div class="surface-stat px-3 py-2">
        <span class="text-kicker !text-text-disabled">{entityTerms.video} Scrapers</span>
        <div class="text-lg font-semibold text-text-primary leading-tight">{videoCount}</div>
      </div>
      <div class="surface-stat px-3 py-2">
        <span class="text-kicker !text-text-disabled">{entityTerms.performers} Scrapers</span>
        <div class="text-lg font-semibold text-text-primary leading-tight">{performerCount}</div>
      </div>
      <div class="surface-stat px-3 py-2">
        <span class="text-kicker !text-text-disabled">StashBox</span>
        <div class="text-lg font-semibold text-text-primary leading-tight">{stashBoxCount}</div>
      </div>
    {:else}
      <div class="surface-stat px-3 py-2">
        <span class="text-kicker !text-text-disabled">Prismedia Plugins</span>
        <div class="text-lg font-semibold text-text-primary leading-tight">
          {prismediaCount}
        </div>
      </div>
    {/if}
  </div>

  {#if error}
    <div
      class="surface-well border-l-2 border-status-error px-3 py-2 text-sm text-status-error-text flex items-center gap-2"
    >
      <span class="flex-1">{error}</span>
      <button
        onclick={onDismissError}
        aria-label="Dismiss error"
        class="text-text-disabled hover:text-text-muted"
      >
        <X class="h-3 w-3" />
      </button>
    </div>
  {/if}
  {#if message && !error}
    <div
      class="surface-well border-l-2 border-status-success px-3 py-2 text-sm text-status-success-text"
    >
      {message}
    </div>
  {/if}

  {#if loading}
    <div class="flex items-center justify-center py-20">
      <Loader2 class="h-6 w-6 animate-spin text-text-muted" />
    </div>
  {:else}
    <div class="flex items-center gap-1 overflow-x-auto scrollbar-hidden">
      {#each visibleTabs as t (t.key)}
        {@const Icon = tabIcon(t.key)}
        <button
          onclick={() => onTabChange(t.key)}
          class={"flex items-center gap-2 px-4 py-2 text-sm font-medium rounded-sm transition-all duration-fast whitespace-nowrap " +
            (tab === t.key
              ? "bg-accent-950 text-text-accent border border-border-accent shadow-[var(--shadow-glow-accent)]"
              : "text-text-muted border border-transparent hover:text-text-secondary hover:bg-surface-3/40")}
        >
          <Icon class="h-3.5 w-3.5" />
          {t.label}
          {#if t.nsfw}
            <span
              class="tag-chip text-[0.5rem] bg-status-error/10 text-status-error-text border border-status-error/20 px-1 py-0"
            >
              NSFW
            </span>
          {/if}
          {#if t.count != null && t.count > 0}
            <span class="text-mono-sm text-text-disabled ml-1">{t.count}</span>
          {/if}
        </button>
      {/each}
    </div>

    {@render children()}
  {/if}
</div>
