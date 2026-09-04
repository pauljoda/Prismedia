<script lang="ts">
  import { ChoiceGroup } from "@prismedia/ui-svelte";
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
  const options = $derived([
    { value: ENTITY_GRID_ALL_KINDS, label: "All", icon: Layers, count: totalCount },
    ...tabs.map(tab => ({ value: tab.kind, label: tab.label, count: tab.count, icon: entityKindIcon(tab.kind), iconColor: entityAccentForKind(tab.kind).primary })),
  ]);
</script>
{#if tabs.length > 1}
  <ChoiceGroup type="single" {options} value={activeKind} onValueChange={onActiveKindChange} ariaLabel="Entity kinds" class="entity-kind-filters" />
{/if}
