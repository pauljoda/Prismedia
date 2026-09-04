<script lang="ts">
  import { assetUrl } from "$lib/api/orval-fetch";
  import { buildHrefWithFrom } from "$lib/back-navigation";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";
  import type { SearchResultItem } from "$lib/search/models";
  import { SEARCH_KIND_CONFIG } from "./search-kind-config";

  interface Props {
    item: SearchResultItem;
    index?: number;
    currentPath?: string;
    highlighted?: boolean;
  }

  let {
    item,
    index = 0,
    currentPath,
    highlighted = false,
  }: Props = $props();

  const href = $derived(buildHrefWithFrom(item.href, currentPath ?? ""));
  const label = $derived(SEARCH_KIND_CONFIG[item.kind]?.label ?? item.kind);
  const imageUrl = $derived(assetUrl(item.imagePath));
  const resultCard = $derived(
    entityReferenceToThumbnailCard(
      { id: item.id, kind: item.kind, title: item.title },
      {
        cover: imageUrl ? { src: imageUrl, alt: item.title } : null,
        href,
      },
    ),
  );
  const resultLabel = $derived(item.matchType === "related" ? "Related" : label);
</script>

  <EntityThumbnail
    card={resultCard}
    hoverPreviewsEnabled={false}
    {highlighted}
  >
    {#snippet subtitleContent()}
      {#if item.subtitle}
        <span class="truncate font-mono text-[0.62rem] text-text-muted">{item.subtitle}</span>
      {/if}
      <span class="tag-chip w-fit text-[0.6rem]">{resultLabel}</span>
    {/snippet}
  </EntityThumbnail>
