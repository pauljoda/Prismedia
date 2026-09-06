<script lang="ts">
  import type { EntityDetailDate } from "$lib/entities/entity-detail";
  import { summarizeEntityDates } from "$lib/entities/entity-date-summary";
  import { Popover, buttonVariants } from "@prismedia/ui-svelte";
  import { ChevronDown, X } from "@lucide/svelte";

  /**
   * Concise labeled milestones for Entity headers, with the full date list available on demand.
   * Inherits the shared hero typography; the disclosure uses the shared Popover and Button styles.
   */
  interface Props {
    dates: EntityDetailDate[];
    /** Render a separator before the first date when other meta precedes it. */
    leadingSeparator?: boolean;
  }

  const { dates, leadingSeparator = false }: Props = $props();
  const summary = $derived(summarizeEntityDates(dates));
  const id = $props.id();
</script>

{#each summary as date, i (date.code)}
  {#if leadingSeparator || i > 0}<span class="meta-sep"></span>{/if}
  <span class="meta-item">
    <span class="meta-item-label">{date.label}</span>
    {date.display}
  </span>
{/each}

{#if dates.length > summary.length}
  <Popover.Root>
    <Popover.Trigger class={buttonVariants({ variant: "ghost", size: "sm" })} aria-label={`Show all ${dates.length} dates`}>
      More dates <ChevronDown data-icon="inline-end" aria-hidden="true" />
    </Popover.Trigger>
    <Popover.Content align="start" aria-labelledby={`${id}-title`}>
      <Popover.Header>
        <div class="flex items-center justify-between gap-control-gap">
          <Popover.Title id={`${id}-title`}>Dates</Popover.Title>
          <Popover.Close class={buttonVariants({ variant: "ghost", size: "icon-sm" })} aria-label="Close dates">
            <X aria-hidden="true" />
          </Popover.Close>
        </div>
      </Popover.Header>
      <dl class="date-list">
        {#each dates as date (date.code)}
          <div class="date-row">
            <dt>{date.label}</dt>
            <dd>{date.display}</dd>
          </div>
        {/each}
      </dl>
    </Popover.Content>
  </Popover.Root>
{/if}

<style>
  .date-list { display: grid; gap: var(--spacing-control-gap); margin: 0; }
  .date-row { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: var(--spacing-control-gap); align-items: baseline; }
  dt { color: var(--color-text-muted); font-size: var(--text-caption); overflow-wrap: anywhere; }
  dd { margin: 0; font-size: var(--text-label); font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
</style>
