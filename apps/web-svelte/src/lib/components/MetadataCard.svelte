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
  }

  let { title, icon: Icon, rows, children }: Props = $props();
</script>

<div class="metadata-card">
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
          <dt>{row.label}</dt>
          <dd>{row.value}</dd>
        </div>
      {/each}
    </dl>
  {/if}
</div>

<style>
  .metadata-card {
    min-width: 0;
    padding: 0.65rem 0.85rem;
    border: 1px solid var(--color-border-default, rgba(164, 172, 185, 0.12));
    border-radius: var(--radius-sm, 6px);
    background: var(--color-surface-2, #11161d);
  }

  .metadata-card-title {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    margin: 0 0 0.45rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--color-text-disabled, #5f687a);
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
    grid-template-columns: minmax(4.5rem, max-content) minmax(0, 1fr);
    gap: 0.65rem;
    align-items: baseline;
    padding: 0.3rem 0;
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
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.65rem;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
  }

  .metadata-card-row dd {
    margin: 0;
    min-width: 0;
    overflow-wrap: anywhere;
    color: var(--color-text-secondary, #c4c9d4);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    font-weight: 500;
  }
</style>
