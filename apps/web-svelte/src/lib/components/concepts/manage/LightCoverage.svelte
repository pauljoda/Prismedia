<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import { bandGradient } from "./media-families";
  import type { FamilyCoverage } from "./plugin-light";

  interface Props {
    coverage: FamilyCoverage[];
    class?: string;
  }

  let { coverage, class: className }: Props = $props();
  const lit = $derived(coverage.filter((entry) => entry.sources.length > 0).length);
</script>

<!--
  The library's spectrum as the installed plugins light it. A lit band means at least one plugin can
  identify or fetch that family; a dark band is a gap worth knowing about.
-->
<section
  class={cn(
    "flex flex-col gap-4 rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] p-4",
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
  <ul class="grid grid-cols-2 gap-x-4 gap-y-3.5 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6">
    {#each coverage as entry (entry.family.key)}
      {@const covered = entry.sources.length > 0}
      <li class="flex min-w-0 flex-col gap-1.5">
        <span
          class="h-1 rounded-[2px]"
          style:background={covered ? bandGradient(entry.family.accent) : "var(--color-surface-3)"}
          aria-hidden="true"
        ></span>
        <span class="flex min-w-0 items-baseline justify-between gap-2">
          <span class={cn("truncate text-caption", covered ? "text-text-secondary" : "text-text-disabled")}>
            {entry.family.label}
          </span>
          <span class="font-mono text-[0.66rem] text-text-muted">{entry.sources.length}</span>
        </span>
        <span class="truncate text-[0.66rem] text-text-disabled" title={entry.sources.join(", ")}>
          {covered ? entry.sources.join(", ") : "—"}
        </span>
      </li>
    {/each}
  </ul>
</section>
