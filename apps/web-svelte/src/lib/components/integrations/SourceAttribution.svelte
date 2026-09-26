<script lang="ts">
  import { Disclosure } from "@prismedia/ui-svelte";
  import type { CatalogAttribution } from "$lib/api/generated/model";
  import MetadataCard from "$lib/components/MetadataCard.svelte";

  let { attribution, compact = false }: { attribution: CatalogAttribution; compact?: boolean } = $props();
  const rows = $derived([
    { label: "Creator", value: attribution.creator },
    { label: "Credit", value: attribution.credit },
    { label: "License", value: attribution.licenseName },
    { label: "Usage terms", value: attribution.usageTerms },
    { label: "Attribution", value: attribution.attributionRequired === true ? "Required by the source"
      : attribution.attributionRequired === false ? "Not required by the source" : null },
  ].flatMap(row => row.value ? [{ label: row.label, value: row.value }] : []));
</script>

{#if compact}
  <div class="min-w-0 space-y-2 text-xs text-text-muted">
  <span class="inline-flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
    {#if attribution.creator}<span class="truncate">{attribution.creator}</span>{/if}
    {#if attribution.licenseName}<span>{attribution.licenseName}</span>{/if}
    <a class="text-text-primary underline decoration-border-default underline-offset-4" href={attribution.sourceUrl} target="_blank" rel="noreferrer noopener">Source</a>
    {#if attribution.licenseUrl}
      <a class="text-text-primary underline decoration-border-default underline-offset-4" href={attribution.licenseUrl} target="_blank" rel="noreferrer noopener">License</a>
    {/if}
  </span>
  {#if attribution.credit || attribution.usageTerms || attribution.attributionRequired !== null}
    <Disclosure title="Source details">
      <div class="space-y-2 text-sm">
        {#if attribution.credit}<p>{attribution.credit}</p>{/if}
        {#if attribution.usageTerms}<p>{attribution.usageTerms}</p>{/if}
        {#if attribution.attributionRequired !== null}<p>{attribution.attributionRequired ? "Attribution required by the source" : "Attribution not required by the source"}</p>{/if}
      </div>
    </Disclosure>
  {/if}
  </div>
{:else}
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
{/if}
