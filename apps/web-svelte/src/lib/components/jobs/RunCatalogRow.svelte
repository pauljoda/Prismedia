<script lang="ts">
  import { Loader2, Play } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import type { RunCatalogEntry } from "$lib/jobs/run-catalog";

  interface Props {
    entry: RunCatalogEntry;
    running: boolean;
    disabled?: boolean;
    onRun: (jobType: string) => void | Promise<void>;
  }

  let {
    entry,
    running,
    disabled = false,
    onRun,
  }: Props = $props();

  const Icon = $derived(entry.icon);
</script>

<div
  class={cn(
    "group flex items-stretch rounded-xs border border-transparent transition-all duration-fast",
    "hover:border-border-accent/30 hover:bg-surface-2/40",
    "focus-within:border-border-accent focus-within:shadow-[var(--shadow-focus-accent)]",
  )}
>
  <Button variant="outline" size="sm"
    type="button"
    onclick={() => void onRun(entry.jobType)}
    disabled={disabled || running}
    class={cn(
      "flex flex-1 items-center gap-3 px-3 py-2 text-left",
      "focus-visible:outline-none",
      "disabled:cursor-not-allowed disabled:opacity-50",
    )}
    title={entry.description}
  >
    <Icon
      class={cn(
        "h-4 w-4 shrink-0 transition-colors",
        "text-text-disabled group-hover:text-text-muted",
      )}
    />
    <div class="min-w-0 flex-1">
      <div class="truncate text-[0.78rem] font-medium text-text-primary">{entry.label}</div>
    </div>
    <span
      class={cn(
        "shrink-0 transition-opacity",
        running
          ? "opacity-100"
          : "opacity-0 group-hover:opacity-70 group-focus-within:opacity-70",
      )}
    >
      {#if running}
        <Loader2 class="h-3.5 w-3.5 animate-spin text-text-accent" />
      {:else}
        <Play class="h-3.5 w-3.5 text-text-accent" />
      {/if}
    </span>
  </Button>

</div>
