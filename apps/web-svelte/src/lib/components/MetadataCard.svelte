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
  }

  let { title, icon: Icon, rows, children, wide = false, capped = false }: Props = $props();
  const cardClass = $derived([
    "metadata-card min-w-0",
    wide ? "metadata-card-wide" : "",
    capped ? "metadata-card-capped" : "",
  ].filter(Boolean).join(" "));

  /** Makes backend-style field names readable while preserving deliberate codes such as TMDB. */
  function displayLabel(label: string): string {
    if (!/[_-]|[a-z\d][A-Z]/.test(label)) return label;
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
        <Icon class="text-muted-foreground" aria-hidden="true" />
      {/if}
      {title}
    </Card.Title>
  </Card.Header>
  {#if children}
    <Card.Content>
      <div class="metadata-card-body">
        {@render children()}
      </div>
    </Card.Content>
  {:else if rows && rows.length > 0}
    <Card.Content>
      <dl class="metadata-card-rows">
        {#each rows as row (row.label)}
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

  :global(.metadata-card-capped) .metadata-card-body {
    min-height: 0;
    overflow-y: auto;
    overscroll-behavior: contain;
    padding-right: 0.25rem;
    scrollbar-gutter: stable;
  }

  .metadata-card-rows {
    display: grid;
    gap: 0;
    margin: 0;
  }

  .metadata-card-row {
    display: grid;
    grid-template-columns: minmax(4.5rem, max-content) minmax(0, 1fr);
    gap: 0.85rem;
    align-items: baseline;
    padding: 0.45rem 0;
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
    font-size: 0.75rem;
    font-weight: 500;
  }

  .metadata-card-row dd {
    margin: 0;
    min-width: 0;
    overflow-wrap: anywhere;
    color: var(--color-text, #f4efe6);
    font-family: var(--font-body, Inter, sans-serif);
    font-size: 0.8125rem;
    font-weight: 500;
    font-variant-numeric: tabular-nums;
  }
</style>
