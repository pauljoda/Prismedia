<script lang="ts">
  import type { EntityDetailTag } from "$lib/entities/entity-detail";

  interface Props {
    label?: string;
    tags: EntityDetailTag[];
  }

  let { label = "Tags:", tags }: Props = $props();
</script>

{#if tags.length > 0}
  <div class="tags-row">
    <span class="tags-label">{label}</span>
    {#each tags as tag (tag.id)}
      {#if tag.href}
        <a class="tag-chip tag-chip-default tag-link" href={tag.href}>{tag.title}</a>
      {:else}
        <span class="tag-chip tag-chip-default tag-link is-static">{tag.title}</span>
      {/if}
    {/each}
  </div>
{/if}

<style>
  .tags-row {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.35rem;
    padding-top: 0.25rem;
  }

  .tags-label {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--detail-text-muted);
    margin-right: 0.25rem;
  }

  .tag-link {
    display: inline-flex;
    align-items: center;
    text-decoration: none;
    transition: border-color 0.15s, color 0.15s, box-shadow 0.15s;
  }

  .tag-link:not(.is-static):hover,
  .tag-link:not(.is-static):focus-visible {
    color: var(--detail-text);
    border-color: var(--detail-accent-muted);
    box-shadow: 0 0 16px var(--detail-accent-glow);
    outline: none;
  }
</style>
