<script lang="ts">
  import { Disclosure } from "@prismedia/ui-svelte";
  import type { CatalogAttribution } from "$lib/api/generated/model";
  import MetadataCard from "$lib/components/MetadataCard.svelte";

  let { attribution }: { attribution: CatalogAttribution } = $props();
  const rows = $derived([
    { label: "Creator", value: attribution.creator },
    { label: "Credit", value: attribution.credit },
    { label: "License", value: attribution.licenseName },
    { label: "Usage terms", value: attribution.usageTerms },
    { label: "Attribution", value: attribution.attributionRequired === true ? "Required by the source"
      : attribution.attributionRequired === false ? "Not required by the source" : null },
  ].flatMap(row => row.value ? [{ label: row.label, value: row.value }] : []));
</script>

<Disclosure title="Source attribution">
  <div class="min-w-0 space-y-3">
    {#if rows.length}<MetadataCard title="Source statements" {rows} stacked />{/if}
    <div class="flex flex-wrap gap-x-4 gap-y-2 text-sm">
      <a class="underline underline-offset-4" href={attribution.sourceUrl} target="_blank" rel="noreferrer noopener">Source page</a>
      {#if attribution.licenseUrl}
        <a class="underline underline-offset-4" href={attribution.licenseUrl} target="_blank" rel="noreferrer noopener">License details</a>
      {/if}
    </div>
  </div>
</Disclosure>
