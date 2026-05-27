<script lang="ts">
  import type { LucideIcon } from "@lucide/svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import type { EntityDetailCredit } from "$lib/entities/entity-detail";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";

  interface Props {
    icon: LucideIcon;
    references: EntityDetailCredit[];
    title: string;
  }

  let { icon: Icon, references, title }: Props = $props();
</script>

{#if references.length > 0}
  <section class="detail-section">
    <h2 class="section-label">
      <Icon class="h-4 w-4" />
      {title}
    </h2>
    <div class="reference-list is-horizontal-rail" aria-label={title}>
      {#each references as reference, index (`${reference.id}:${index}`)}
        <div class="reference-thumbnail">
          <EntityThumbnail
            card={entityReferenceToThumbnailCard({
              id: reference.id,
              kind: reference.kind,
              title: reference.title,
              thumbnailUrl: reference.thumbnail,
            })}
            selectable={false}
            titleAlign="center"
            titleSize="compact"
          />
        </div>
      {/each}
    </div>
  </section>
{/if}

<style>
  .detail-section {
    padding: 1rem 0;
    border-bottom: 1px solid var(--detail-border, #1c2235);
  }

  .detail-section:last-child {
    border-bottom: none;
    padding-bottom: 0;
  }

  .section-label {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    margin: 0 0 0.75rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--detail-text-muted, #8a93a6);
  }

  .reference-list {
    display: flex;
    flex-wrap: nowrap;
    align-items: stretch;
    gap: 0.75rem;
    width: 100%;
    max-width: 100%;
    min-width: 0;
    overflow-x: auto;
    overflow-y: hidden;
    padding-bottom: 0.35rem;
    scroll-padding-inline: 0.25rem;
    scrollbar-width: thin;
  }

  .reference-thumbnail {
    flex: 0 0 clamp(7rem, 33vw, 8.75rem);
    min-width: 0;
  }
</style>
