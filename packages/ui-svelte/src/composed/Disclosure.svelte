<script lang="ts">
  import type { Component, Snippet } from "svelte";
  import { ChevronRight } from "@lucide/svelte";
  import * as Collapsible from "../components/ui/collapsible";
  import Badge from "../primitives/Badge.svelte";
  import { cn } from "../lib/utils";

  /** A quiet, keyboard-accessible section for supporting details and actions. */
  let {
    title, icon: Icon, count, open = $bindable(false), children, class: className,
  }: {
    title: string;
    icon?: Component;
    count?: number;
    open?: boolean;
    children: Snippet;
    class?: string;
  } = $props();
</script>

<Collapsible.Root bind:open class={cn("min-w-0 rounded-md border border-border bg-card", className)}>
  <Collapsible.Trigger class="group flex min-h-11 w-full items-center gap-2.5 rounded-md px-4 py-2 text-left text-sm font-medium text-foreground transition-colors hover:bg-accent/50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
    {#if Icon}<Icon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />{/if}
    <span class="min-w-0 flex-1">{title}</span>
    {#if count !== undefined}<Badge variant="default">{count}</Badge>{/if}
    <ChevronRight class="size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-90 motion-reduce:transition-none" aria-hidden="true" />
  </Collapsible.Trigger>
  <Collapsible.Content>
    <div class="min-w-0 border-t border-border px-4 py-3">{@render children()}</div>
  </Collapsible.Content>
</Collapsible.Root>
