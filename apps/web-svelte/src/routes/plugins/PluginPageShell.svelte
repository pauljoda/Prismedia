<script lang="ts">
  import type { Snippet } from "svelte";
  import { AlertTriangle, Boxes, Check, Globe, Plug, Puzzle, Sparkles, X } from "@lucide/svelte";
  import { Alert, Badge, Button, Tabs } from "@prismedia/ui-svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
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

<Tabs.Root value={tab} onValueChange={(value) => {
  const next = visibleTabs.find((item) => item.key === value);
  if (next) onTabChange(next.key);
}} class="gap-5 min-w-0">
  <header class="flex flex-col gap-4">
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
      <Tabs.List variant="line" class="overflow-x-auto scrollbar-hidden" aria-label="Plugin views">
        {#each visibleTabs as t (t.key)}
          {@const Icon = tabIcon(t.key)}
          <Tabs.Trigger value={t.key}>
            <Icon />
            {t.label}
            {#if t.nsfw}
              <Badge variant="destructive">NSFW</Badge>
            {/if}
            {#if t.count != null && t.count > 0}
              <Badge>{t.count}</Badge>
            {/if}
          </Tabs.Trigger>
        {/each}
      </Tabs.List>
    {/if}
  </header>

  {#if error}
    <Alert.Root variant="destructive">
      <AlertTriangle />
      <Alert.Description>{error}</Alert.Description>
      <Alert.Action>
      <Button
        variant="ghost"
        size="icon"
        onclick={onDismissError}
        aria-label="Dismiss error"
      >
        <X />
      </Button>
      </Alert.Action>
    </Alert.Root>
  {/if}
  {#if message && !error}
    <Alert.Root role="status">
      <Check />
      <Alert.Description>{message}</Alert.Description>
    </Alert.Root>
  {/if}

  {#if loading}
    <StatePlaceholder icon={Puzzle} title="Loading plugins" busy />
  {:else}
    <Tabs.Content value={tab}>{@render children()}</Tabs.Content>
  {/if}
</Tabs.Root>
