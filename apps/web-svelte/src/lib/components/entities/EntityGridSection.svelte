<script lang="ts">
  import { browser } from "$app/environment";
  import { Collapsible, Badge, buttonVariants, cn } from "@prismedia/ui-svelte";
  import { ChevronDown } from "@lucide/svelte";
  import type { Component, Snippet } from "svelte";

  type IconComponent = Component<{ class?: string }>;

  interface Props {
    children: Snippet;
    count: number;
    icon?: IconComponent;
    prefsKey: string;
    title: string;
  }

  let {
    children,
    count,
    icon: Icon,
    prefsKey,
    title,
  }: Props = $props();

  const storageKey = $derived(`prismedia:entity-grid-section:${prefsKey}`);
  const contentId = $derived(`entity-grid-section-${slugify(prefsKey)}`);
  let collapsed = $derived(readStoredCollapsed(storageKey));

  function readStoredCollapsed(key: string): boolean {
    if (!browser) return false;
    try {
      return window.localStorage.getItem(key) === "collapsed";
    } catch {
      return false;
    }
  }

  function writeStoredCollapsed(next: boolean): void {
    if (!browser) return;
    try {
      window.localStorage.setItem(storageKey, next ? "collapsed" : "expanded");
    } catch {
      // Section collapse is a convenience preference; storage failures should not block interaction.
    }
  }

  function setOpen(open: boolean) {
    collapsed = !open;
    writeStoredCollapsed(collapsed);
  }

  function slugify(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "grid";
  }
</script>

<Collapsible.Root open={!collapsed} onOpenChange={setOpen} class="grid gap-3">
  <h2 class="m-0">
  <Collapsible.Trigger class={cn(buttonVariants({ variant: "ghost" }), "h-auto w-full justify-between px-1 py-1 font-heading text-lg font-semibold")} title={collapsed ? `Expand ${title}` : `Collapse ${title}`}>
    <span class="flex min-w-0 items-center gap-2">
      {#if Icon}
        <Icon class="h-4 w-4" />
      {/if}
      <span class="whitespace-normal text-left">{title}</span>
      <Badge variant="secondary" class="font-mono text-xs">{count}</Badge>
    </span>
    <ChevronDown class={cn("size-4 shrink-0 transition-transform", !collapsed && "rotate-180")} />
  </Collapsible.Trigger>
  </h2>

  <Collapsible.Content id={contentId} class="min-w-0">
    {#if !collapsed}
      {@render children()}
    {/if}
  </Collapsible.Content>
</Collapsible.Root>
