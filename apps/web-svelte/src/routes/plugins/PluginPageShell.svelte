<script lang="ts">
  import type { Snippet } from "svelte";
  import { Boxes, Globe, Loader2, Plug, Puzzle, Sparkles, X } from "@lucide/svelte";
  import type { PluginTabDefinition, PluginsTab } from "./plugin-page-types";

  let {
    loading,
    error,
    message,
    tab,
    visibleTabs,
    onDismissError,
    onTabChange,
    children,
  }: {
    loading: boolean;
    error: string | null;
    message: string | null;
    tab: PluginsTab;
    visibleTabs: PluginTabDefinition[];
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

<div class="space-y-5">
  <header class="space-y-4 border-b border-border-subtle pb-4">
    <div>
      <h1 class="flex items-center gap-2.5">
        <Puzzle class="h-5 w-5 text-text-accent" />
        Plugins
      </h1>
      <p class="mt-1 text-text-muted text-[0.78rem]">
        Install and manage identification plugins and metadata providers
      </p>
    </div>

    {#if !loading}
      <nav class="flex items-center gap-1 overflow-x-auto scrollbar-hidden">
        {#each visibleTabs as t (t.key)}
          {@const Icon = tabIcon(t.key)}
          {@const active = tab === t.key}
          <button
            onclick={() => onTabChange(t.key)}
            class={"flex items-center gap-2 px-3.5 py-2 text-sm font-medium rounded-sm transition-all duration-fast whitespace-nowrap " +
              (active
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
              <span
                class={"text-mono-sm tabular-nums rounded-xs px-1.5 py-px " +
                  (active
                    ? "bg-accent-900/60 text-text-accent"
                    : "bg-surface-3/60 text-text-disabled")}
              >
                {t.count}
              </span>
            {/if}
          </button>
        {/each}
      </nav>
    {/if}
  </header>

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
    {@render children()}
  {/if}
</div>
