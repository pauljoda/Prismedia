<script lang="ts">
  import { ChevronDown } from "@lucide/svelte";
  import { Button, Checkbox, Collapsible, cn } from "@prismedia/ui-svelte";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import {
    proposalFieldValue,
    proposalHasField,
    reviewBaseFieldKeys,
    reviewDateTypeLabel,
    reviewFieldLabels,
  } from "$lib/components/identify-review";
  import { METADATA_PATCH_FIELD } from "$lib/entities/entity-codes";
  import ReviewFieldValue from "./ReviewFieldValue.svelte";

  interface Props {
    proposal: EntityMetadataProposal;
    selectedFields: Record<string, boolean>;
    currentValue: (field: string) => string;
    onFieldChange: (field: string, selected: boolean) => void;
    onAllFields: (selected: boolean) => void;
  }

  let { proposal, selectedFields, currentValue, onFieldChange, onAllFields }: Props = $props();

  let showUnchanged = $state(false);

  /**
   * Order, repetition, whitespace, and code-versus-label spellings are not changes worth reviewing:
   * a value compares as the set of its readable parts.
   */
  function parts(field: string, value: string): Set<string> {
    return new Set(
      readableCurrent(field, value)
        .split(/[,;]\s*/)
        .map((part) => part.replace(/\s+/g, " ").trim().toLowerCase())
        .filter(Boolean),
    );
  }

  function sameValue(field: string, current: string, proposed: string): boolean {
    const left = parts(field, current);
    const right = parts(field, proposed);
    return left.size === right.size && [...left].every((part) => right.has(part));
  }

  /** Current dates arrive as "type: value" pairs; they read with the same labels as the proposal. */
  function readableCurrent(field: string, value: string): string {
    if (field !== METADATA_PATCH_FIELD.dates) return value;
    return value
      .split(/,\s*/)
      .map((pair) => {
        const [type, ...rest] = pair.split(": ");
        return rest.length > 0 ? `${reviewDateTypeLabel(type ?? "")}: ${rest.join(": ")}` : pair;
      })
      .join(", ");
  }

  const rows = $derived(
    reviewBaseFieldKeys(proposal)
      .filter((field) => proposalHasField(proposal, field))
      .map((field) => {
        const current = readableCurrent(field, currentValue(field).trim());
        const unchanged = current.length > 0 && sameValue(field, current, proposalFieldValue(proposal, field));
        return { field, current, unchanged };
      }),
  );
  const changes = $derived(rows.filter((row) => !row.unchanged));
  const unchanged = $derived(rows.filter((row) => row.unchanged));
  const selectedCount = $derived(rows.filter((row) => selectedFields[row.field]).length);
</script>

{#snippet row(entry: { field: string; current: string; unchanged: boolean })}
  {@const label = reviewFieldLabels[entry.field] ?? entry.field}
  <li class="grid min-w-0 grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1 px-4 py-3 sm:grid-cols-[auto_8rem_minmax(0,1fr)]">
    <Checkbox
      size="md"
      class="mt-0.5"
      checked={selectedFields[entry.field] ?? false}
      aria-label="Accept {label}"
      onchange={(checked) => onFieldChange(entry.field, checked)}
    />
    <span class="pt-0.5 text-caption text-text-muted">{label}</span>
    <div class={cn("col-start-2 min-w-0 text-sm text-text-primary sm:col-start-3 sm:row-start-1", !selectedFields[entry.field] && "opacity-50")}>
      <ReviewFieldValue {proposal} field={entry.field} />
      {#if entry.current && !entry.unchanged}
        <p class="mt-1.5 line-clamp-2 text-caption text-text-muted">
          <span class="font-mono text-[0.62rem] uppercase tracking-[0.1em] text-text-disabled">Now</span>
          {entry.current}
        </p>
      {/if}
    </div>
  </li>
{/snippet}

<section class="min-w-0 overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)]" aria-labelledby="review-details-{proposal.proposalId}">
  <header class="flex items-center justify-between gap-3 border-b border-[var(--color-border-subtle)] px-4 py-2.5">
    <h3 id="review-details-{proposal.proposalId}" class="font-heading text-sm font-semibold text-text-primary">
      Details <span class="ml-1 font-mono text-caption font-normal text-text-muted">{selectedCount}/{rows.length}</span>
    </h3>
    <div class="flex items-center gap-1">
      <Button variant="ghost" size="xs" onclick={() => onAllFields(true)}>All</Button>
      <Button variant="ghost" size="xs" onclick={() => onAllFields(false)}>None</Button>
    </div>
  </header>
  <ul class="divide-y divide-[var(--color-border-subtle)]">
    {#each changes as entry (entry.field)}
      {@render row(entry)}
    {/each}
  </ul>
  {#if unchanged.length > 0}
    <Collapsible.Root bind:open={showUnchanged}>
      <Collapsible.Trigger
        class="flex w-full items-center justify-between gap-3 border-t border-[var(--color-border-subtle)] px-4 py-2.5 text-left text-caption text-text-muted transition-colors hover:text-text-primary"
      >
        <span><span class="font-mono text-text-secondary">{unchanged.length}</span> unchanged</span>
        <ChevronDown class={cn("size-3.5 transition-transform motion-reduce:transition-none", showUnchanged && "rotate-180")} aria-hidden="true" />
      </Collapsible.Trigger>
      <Collapsible.Content>
        <ul class="divide-y divide-[var(--color-border-subtle)] border-t border-[var(--color-border-subtle)]">
          {#each unchanged as entry (entry.field)}
            {@render row(entry)}
          {/each}
        </ul>
      </Collapsible.Content>
    </Collapsible.Root>
  {/if}
</section>
