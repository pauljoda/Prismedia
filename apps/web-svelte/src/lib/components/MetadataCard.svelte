<script lang="ts" module>
  export interface MetadataRow {
    label: string;
    value: string;
  }
</script>

<script lang="ts">
  import type { Component, Snippet } from "svelte";

  interface Props {
    title: string;
    icon?: Component<Record<string, unknown>>;
    rows?: MetadataRow[];
    children?: Snippet;
    wide?: boolean;
    capped?: boolean;
  }

  let { title, icon: Icon, rows, children, wide = false, capped = false }: Props = $props();

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

<div class="metadata-card" class:metadata-card-wide={wide} class:metadata-card-capped={capped}>
  <h3 class="metadata-card-title">
    {#if Icon}
      <Icon class="h-3.5 w-3.5" />
    {/if}
    {title}
  </h3>
  {#if children}
    <div class="metadata-card-body">
      {@render children()}
    </div>
  {:else if rows && rows.length > 0}
    <dl class="metadata-card-rows">
      {#each rows as row (row.label)}
        <div class="metadata-card-row">
          <dt>{displayLabel(row.label)}</dt>
          <dd>{row.value}</dd>
        </div>
      {/each}
    </dl>
  {/if}
</div>

<style>
  .metadata-card {
    min-width: 0;
    padding: 0.95rem 1rem;
    border: 1px solid var(--color-border-default, rgba(164, 172, 185, 0.12));
    border-radius: var(--radius-md, 8px);
    background: color-mix(in srgb, var(--color-surface-2, #11161d) 78%, transparent);
  }

  .metadata-card-capped {
    display: grid;
    grid-template-rows: auto minmax(0, 1fr);
    max-height: var(--metadata-card-max-height, 24rem);
  }

  .metadata-card-title {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin: 0 0 0.7rem;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.8125rem;
    font-weight: 600;
    letter-spacing: -0.01em;
    color: var(--color-text-secondary, #c4c9d4);
  }

  .metadata-card-title :global(svg) {
    width: 1rem;
    height: 1rem;
    color: var(--color-text-muted, #8a93a6);
  }

  .metadata-card-body {
    min-width: 0;
  }

  .metadata-card-capped .metadata-card-body {
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
