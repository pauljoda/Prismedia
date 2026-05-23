<script lang="ts">
  import type { Snippet } from "svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    children: Snippet;
    sidebar?: Snippet;
    class?: string;
  }

  let { children, sidebar, class: className }: Props = $props();
</script>

<div class={cn("grid grid-cols-1 lg:grid-cols-3 gap-5", className)}>
  <div class={cn("space-y-5", sidebar ? "lg:col-span-2" : "lg:col-span-3")}>
    {@render children()}
  </div>
  {#if sidebar}
    <div class="surface-panel p-4 space-y-3 h-fit">{@render sidebar()}</div>
  {/if}
</div>
