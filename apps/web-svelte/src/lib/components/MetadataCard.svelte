<script lang="ts" module>
  export interface MetadataRow {
    label: string;
    value: string;
  }
</script>

<script lang="ts">
  import type { Component, Snippet } from "svelte";
  import { Card } from "@prismedia/ui-svelte";

  interface Props {
    title: string;
    icon?: Component<Record<string, unknown>>;
    rows?: MetadataRow[];
    children?: Snippet;
    wide?: boolean;
    capped?: boolean;
    /** Place values below their labels when paths or identifiers need the full card width. */
    stacked?: boolean;
    /** Use the shared utility typeface for literal paths, hashes, and source identifiers. */
    monospace?: boolean;
  }

  let { title, icon: Icon, rows, children, wide = false, capped = false, stacked = false, monospace = false }: Props = $props();
  const cardClass = $derived([
    "metadata-card min-w-0",
    wide ? "metadata-card-wide" : "",
    capped ? "metadata-card-capped" : "",
  ].filter(Boolean).join(" "));

  /** Makes backend-style field names readable while preserving deliberate codes such as TMDB. */
  function displayLabel(label: string): string {
    const words = label
      .replace(/([a-z\d])([A-Z])/g, "$1 $2")
      .replace(/[_-]+/g, " ")
      .trim();
    return words ? words[0].toUpperCase() + words.slice(1) : label;
  }
</script>

<Card.Root size="sm" class={cardClass}>
  <Card.Header>
    <Card.Title role="heading" aria-level={3} class="flex items-center gap-2 text-foreground">
      {#if Icon}
        <Icon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      {/if}
      {title}
    </Card.Title>
  </Card.Header>
  {#if children}
    <Card.Content class={capped ? "min-h-0 overflow-y-auto overscroll-contain" : undefined}>
      <div class="metadata-card-body">
        {@render children()}
      </div>
    </Card.Content>
  {:else if rows && rows.length > 0}
    <Card.Content class={capped ? "min-h-0 overflow-y-auto overscroll-contain" : undefined}>
      <dl class="metadata-card-rows" class:is-stacked={stacked} class:is-monospace={monospace}>
        <!-- Read-only rows have no unique identity: labels and even complete rows may repeat. -->
        {#each rows as row}
          <div class="metadata-card-row">
            <dt>{displayLabel(row.label)}</dt>
            <dd>{row.value}</dd>
          </div>
        {/each}
      </dl>
    </Card.Content>
  {/if}
</Card.Root>

<style>
  :global(.metadata-card-capped) {
    max-height: var(--metadata-card-max-height, 24rem);
  }

  .metadata-card-body {
    min-width: 0;
  }

  .metadata-card-rows {
    display: grid;
    gap: 0;
    margin: 0;
  }

  .metadata-card-row {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1.25fr);
    gap: var(--spacing-control-gap);
    align-items: baseline;
    padding: var(--spacing-control-gap) 0;
    border-bottom: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.07));
  }

  .metadata-card-row:last-child {
    border-bottom: none;
    padding-bottom: 0;
  }

  .metadata-card-row:first-child {
    padding-top: 0;
  }

  .metadata-card-row dt {
    color: var(--color-text-muted, #8a93a6);
    font-family: var(--font-body, Inter, sans-serif);
    font-size: var(--text-caption);
    font-weight: 500;
    overflow-wrap: anywhere;
  }

  .metadata-card-row dd {
    margin: 0;
    min-width: 0;
    overflow-wrap: anywhere;
    color: var(--color-text-primary);
    font-family: var(--font-body, Inter, sans-serif);
    font-size: var(--text-label);
    font-weight: 500;
    font-variant-numeric: tabular-nums;
  }

  .is-stacked .metadata-card-row {
    grid-template-columns: minmax(0, 1fr);
    gap: var(--spacing);
  }

  .is-monospace dd {
    font-family: var(--font-mono);
    font-size: var(--text-caption);
    font-weight: 400;
    user-select: text;
  }
</style>
