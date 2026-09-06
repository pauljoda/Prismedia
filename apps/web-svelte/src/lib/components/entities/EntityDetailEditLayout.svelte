<script lang="ts">
  import { Field, Separator } from "@prismedia/ui-svelte";
  import type { Snippet } from "svelte";
  import type { EntityDetailSection } from "./entity-detail-types";

  let { sections, item }: {
    sections: EntityDetailSection[];
    item: Snippet<[EntityDetailSection]>;
  } = $props();

  // References have their own reading/editing flow; keep them beside the main fields,
  // not interleaved across full-width rows. Unknown/custom sections stay in the main flow.
  const referenceIds = new Set(["links", "source", "sources", "fingerprints", "technical"]);
  const fields = $derived(sections.filter(section => !referenceIds.has(section.id)));
  const references = $derived(sections.filter(section => referenceIds.has(section.id)));
</script>

<div class="entity-edit-layout @container w-full min-w-0">
  <div class={['grid min-w-0 gap-8', fields.length && references.length && '@min-[56rem]:grid-cols-2']}>
    {#if fields.length}
      <section aria-label="Editable fields" class="min-w-0">
        <Field.Group class="gap-6">
          {#each fields as section, index (section.id)}
            {#if index > 0}<Separator class="bg-border-subtle" />{/if}
            {@render item(section)}
          {/each}
        </Field.Group>
      </section>
    {/if}
    {#if references.length}
      <section aria-label="References" class="min-w-0">
        <Field.Group class="gap-6">
          {#each references as section, index (section.id)}
            {#if index > 0}<Separator class="bg-border-subtle" />{/if}
            {@render item(section)}
          {/each}
        </Field.Group>
      </section>
    {/if}
  </div>
</div>
