<script lang="ts">
  import { ToggleGroup } from "@prismedia/ui-svelte";
  import { Layers } from "@lucide/svelte";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { entityKindIcon } from "$lib/entities/entity-kind-icons";
  import { ENTITY_GRID_ALL_KINDS, type EntityGridKindTab } from "$lib/entities/entity-grid";

  interface Props {
    activeKind: string;
    onActiveKindChange: (kind: string) => void;
    tabs: EntityGridKindTab[];
    totalCount: number;
  }

  let { activeKind, onActiveKindChange, tabs, totalCount }: Props = $props();
</script>
{#if tabs.length > 1}
  <ToggleGroup.Root type="single" value={activeKind} onValueChange={next => { if (next) onActiveKindChange(next); }} aria-label="Entity kinds" class="entity-kind-filters max-w-full flex-wrap justify-start" variant="outline" spacing={2}>
    <ToggleGroup.Item value={ENTITY_GRID_ALL_KINDS} class="gap-2">
      <Layers aria-hidden="true" />
      All <span class="font-mono text-xs tabular-nums text-muted-foreground">{totalCount}</span>
    </ToggleGroup.Item>
    {#each tabs as tab (tab.kind)}
      {@const Icon = entityKindIcon(tab.kind)}
      <ToggleGroup.Item value={tab.kind} class="gap-2">
        <Icon color={entityAccentForKind(tab.kind).primary} aria-hidden="true" />
        {tab.label} <span class="font-mono text-xs tabular-nums text-muted-foreground">{tab.count}</span>
      </ToggleGroup.Item>
    {/each}
  </ToggleGroup.Root>
{/if}
