<script lang="ts">
  import { Button, cn } from "@prismedia/ui-svelte";
  import { bandGradient } from "$lib/entities/media-families";
  import type { FamilyCoverage } from "$lib/plugins/plugin-families";

  interface Props {
    coverage: FamilyCoverage[];
    /** The family the plugin list is narrowed to, if any. */
    selected?: string | null;
    /** Narrows the list to one family; selecting it again clears the filter. */
    onSelect?: (familyKey: string | null) => void;
    class?: string;
  }

  let { coverage, selected = null, onSelect, class: className }: Props = $props();
  const lit = $derived(coverage.filter((entry) => entry.sources.length > 0).length);
</script>

<!--
  The library's families as the enabled plugins cover them. A lit band means at least one plugin can
  identify or fetch that family; a dark band is a gap. Each family doubles as a filter for the cards.
-->
<section
  class={cn(
    "flex flex-col gap-3 rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] p-4",
    className,
  )}
  aria-labelledby="plugin-coverage-title"
>
  <div class="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
    <h2 id="plugin-coverage-title" class="font-heading text-sm font-semibold text-text-primary">Coverage</h2>
    <p class="font-mono text-[0.72rem] text-text-muted">
      <span class="text-text-secondary">{lit}</span>/{coverage.length} families
    </p>
  </div>
  <ul class="grid grid-cols-3 gap-1 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6">
    {#each coverage as entry (entry.family.key)}
      {@const covered = entry.sources.length > 0}
      {@const active = selected === entry.family.key}
      <li class="min-w-0">
        <Button
          variant="ghost"
          size="sm"
          class={cn(
            "h-auto w-full min-w-0 flex-col items-stretch gap-1.5 rounded-[var(--radius-sm)] border px-1.5 py-1.5 text-left font-normal disabled:opacity-100 sm:px-2 sm:py-2",
            active
              ? "border-[var(--color-border-default)] bg-[var(--color-surface-3)]"
              : "border-transparent hover:bg-[var(--color-surface-2)]",
            selected && !active && "opacity-60 disabled:opacity-60",
          )}
          aria-pressed={active}
          disabled={!covered || !onSelect}
          title={covered ? entry.sources.join(", ") : undefined}
          onclick={() => onSelect?.(active ? null : entry.family.key)}
        >
          <span
            class="h-1 w-full rounded-[2px]"
            style:background={covered ? bandGradient(entry.family.accent) : "var(--color-surface-3)"}
            aria-hidden="true"
          ></span>
          <span class="flex min-w-0 items-baseline justify-between gap-2">
            <span class={cn("truncate text-caption", covered ? "text-text-secondary" : "text-text-disabled")}>
              {entry.family.label}
            </span>
            <span class="font-mono text-[0.66rem] text-text-muted">{entry.sources.length}</span>
          </span>
          <span class="hidden w-full truncate text-[0.66rem] text-text-disabled sm:block">
            {covered ? entry.sources.join(", ") : "—"}
          </span>
        </Button>
      </li>
    {/each}
  </ul>
</section>
