<script lang="ts">
  import { Info } from "@lucide/svelte";
  import { Button, Checkbox } from "@prismedia/ui-svelte";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import {
    proposalFieldValue,
    proposalHasField,
    reviewDiffFieldKeys,
    reviewFieldLabels,
  } from "$lib/components/identify-review";
  import ReviewSection from "./ReviewSection.svelte";

  interface Props {
    proposal: EntityMetadataProposal;
    selectedFields: Record<string, boolean>;
    onFieldChange: (field: string, selected: boolean) => void;
    onAllFields: (selected: boolean) => void;
    currentValue?: (field: string) => string;
    title?: string;
    selectable?: boolean;
  }

  let {
    proposal,
    selectedFields,
    onFieldChange,
    onAllFields,
    currentValue = () => "",
    title = "Base fields",
    selectable = true,
  }: Props = $props();

  const fields = $derived(reviewDiffFieldKeys.filter((field) => proposalHasField(proposal, field)));
  const selectedCount = $derived(fields.filter((field) => selectedFields[field]).length);
</script>

<ReviewSection
  panelId={`base-fields-${proposal.proposalId}`}
  {title}
  meta={`${selectedCount} of ${fields.length} accepted`}
>
  {#snippet icon()}
    <Info class="h-3.5 w-3.5 text-text-accent" />
  {/snippet}
  {#snippet actions()}
    {#if selectable}
      <Button type="button" variant="ghost" size="sm" class="h-auto px-1.5 py-0.5" onclick={() => onAllFields(true)}>
        All
      </Button>
      <Button type="button" variant="ghost" size="sm" class="h-auto px-1.5 py-0.5" onclick={() => onAllFields(false)}>
        None
      </Button>
    {/if}
  {/snippet}

  <div class="hidden grid-cols-[auto_110px_1fr_1fr] items-center gap-3 border-b border-border-default bg-surface-2 px-3.5 py-1.5 md:grid">
    <span class="w-5"></span>
    <span class="text-kicker">Field</span>
    <span class="text-kicker">Current</span>
    <span class="text-kicker text-text-accent">Proposed</span>
  </div>

  {#each fields as field (field)}
    <div class="grid grid-cols-[auto_minmax(0,1fr)] items-start gap-3 border-b border-border-subtle px-3.5 py-3 last:border-b-0 md:grid-cols-[auto_110px_1fr_1fr]">
      <label class="flex items-center">
        <Checkbox
          size="md"
          checked={selectedFields[field] ?? false}
          disabled={!selectable}
          aria-label={`Accept ${reviewFieldLabels[field] ?? field}`}
          onchange={(event) => onFieldChange(field, event.currentTarget.checked)}
        />
      </label>
      <div class="md:contents">
        <div>
          <span class="font-heading text-[0.76rem] font-semibold text-text-secondary">
            {reviewFieldLabels[field] ?? field}
          </span>
          <span class="ml-2 font-mono text-[0.62rem] text-text-disabled md:ml-0 md:block">{field}</span>
        </div>
        <div class="hidden text-[0.76rem] leading-snug text-text-muted md:block">{currentValue(field) || "—"}</div>
        <div class="mt-1 text-[0.82rem] leading-snug text-text-primary md:mt-0">
          {proposalFieldValue(proposal, field)}
        </div>
      </div>
    </div>
  {/each}
</ReviewSection>
