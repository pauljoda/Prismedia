<script lang="ts">
  import { Badge, badgeVariants, cn } from "@prismedia/ui-svelte";
  import type { EntityDetailTag } from "$lib/entities/entity-detail";

  interface Props {
    label?: string;
    tags: EntityDetailTag[];
  }

  let { label = "Tags", tags }: Props = $props();
</script>

{#if tags.length > 0}
  <div class="tags-row">
    <span class="tags-label">{label}</span>
    {#each tags as tag (tag.id)}
      {#if tag.href}
        <a class={cn(badgeVariants({ variant: "outline" }), "tag-link")} href={tag.href}>{tag.title}</a>
      {:else}
        <Badge variant="outline">{tag.title}</Badge>
      {/if}
    {/each}
  </div>
{/if}

<style>
  .tags-row {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.4rem;
    padding-top: 0.35rem;
  }

  .tags-label {
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.8125rem;
    font-weight: 600;
    letter-spacing: -0.01em;
    color: var(--detail-text-secondary);
    margin-right: 0.35rem;
  }

  .tag-link {
    text-decoration: none;
  }
</style>
