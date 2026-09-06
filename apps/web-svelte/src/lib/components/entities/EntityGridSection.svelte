<script lang="ts">
  import { browser } from "$app/environment";
  import { Collapsible, Card, Badge, buttonVariants, cn } from "@prismedia/ui-svelte";
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
  const sectionId = $props.id();
  const contentId = `${sectionId}-content`;
  const titleId = `${sectionId}-title`;
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

</script>

<Collapsible.Root open={!collapsed} onOpenChange={setOpen} class="min-w-0">
  <Card.Root class="gap-0 py-0">
    <Card.Header class="p-0">
      <Card.Title role="heading" aria-level={2}>
        <Collapsible.Trigger
          class={cn(buttonVariants({ variant: "ghost", size: "lg" }), "h-auto min-h-control-lg w-full justify-between rounded-md px-4 py-3 data-[state=open]:rounded-b-none")}
          title={collapsed ? `Expand ${title}` : `Collapse ${title}`}
        >
          <span class="flex min-w-0 items-center gap-2">
            {#if Icon}<Icon class="size-4" />{/if}
            <span id={titleId} class="whitespace-normal text-left font-heading text-base">{title}</span>
            <Badge variant="secondary" class="font-mono">{count}</Badge>
          </span>
          <span class="flex shrink-0 items-center gap-2 text-muted-foreground">
            <span>{collapsed ? "Show" : "Hide"}</span>
            <ChevronDown class={cn("size-4 transition-transform motion-reduce:transition-none", !collapsed && "rotate-180")} aria-hidden="true" />
          </span>
        </Collapsible.Trigger>
      </Card.Title>
    </Card.Header>
    <Collapsible.Content id={contentId} role="region" aria-labelledby={titleId} class="min-w-0">
      <Card.Content class="border-t border-border-subtle p-3 sm:p-4">
        {#if !collapsed}
          {@render children()}
        {/if}
      </Card.Content>
    </Collapsible.Content>
  </Card.Root>
</Collapsible.Root>
