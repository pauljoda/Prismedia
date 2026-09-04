<script lang="ts">
  import { Badge, Button, buttonVariants, cn } from "@prismedia/ui-svelte";
  import { ChevronDown } from "@lucide/svelte";
  import { aspectRatioForKind, toAspectRatioNumeric } from "$lib/entities/entity-thumbnail";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import type { SearchResultGroup } from "$lib/search/models";
  import { SEARCH_KIND_CONFIG } from "./search-kind-config";
  import SearchResultCard from "./SearchResultCard.svelte";

  let { group, currentPath, topResultId }: { group: SearchResultGroup; currentPath: string; topResultId?: string } = $props();
  const PAGE_SIZE = 20;
  let visibleCount = $state(PAGE_SIZE);
  const config = $derived(SEARCH_KIND_CONFIG[group.kind]);
  const Icon = $derived(config.icon);
  const wide = $derived(toAspectRatioNumeric(aspectRatioForKind(group.kind)) > 1);
  const remaining = $derived(Math.max(0, group.items.length - visibleCount));
  const titleId = $props.id();
</script>

<section aria-labelledby={titleId} class="flex min-w-0 flex-col gap-3">
  <header class="flex items-center justify-between gap-3">
    <h2 id={titleId} class="flex items-center gap-2 font-heading text-base font-medium">
      <Icon class="size-4" color={entityAccentForKind(group.kind).primary} aria-hidden="true" />
      {config.label} <Badge variant="secondary">{group.total}</Badge>
    </h2>
    <a href={config.href} class={buttonVariants({ variant: "ghost", size: "sm" })} aria-label={`Browse all ${config.label.toLowerCase()}`}>Browse all</a>
  </header>
  <div class={cn("grid items-start gap-3", wide ? "grid-cols-1 sm:grid-cols-2 xl:grid-cols-3" : "grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5")}>
    {#each group.items.slice(0, visibleCount) as item (item.id)}
      <SearchResultCard {item} {currentPath} highlighted={item.id === topResultId} />
    {/each}
  </div>
  {#if remaining > 0}
    <div class="flex justify-center">
      <Button variant="outline" onclick={() => { visibleCount += PAGE_SIZE; }}>
        <ChevronDown data-icon="inline-start" /> Show more ({remaining} remaining)
      </Button>
    </div>
  {/if}
</section>
