<script lang="ts">
  import { assetUrl } from "$lib/api/orval-fetch";
  import { buildHrefWithFrom } from "$lib/back-navigation";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import type { SearchResultItem } from "$lib/search/models";

  interface Props {
    item: SearchResultItem;
    currentPath?: string;
    highlighted?: boolean;
  }

  let {
    item,
    currentPath,
    highlighted = false,
  }: Props = $props();

  const href = $derived(buildHrefWithFrom(item.href, currentPath ?? ""));
  const imageUrl = $derived(assetUrl(item.imagePath));
  const thumbnailCard = $derived(
    item.thumbnail ? entityCardToThumbnailCard(item.thumbnail, href) : entityReferenceToThumbnailCard(
      { id: item.id, kind: item.kind, title: item.title },
      {
        cover: imageUrl ? { src: imageUrl, alt: item.title } : null,
        href,
      },
    ),
  );
  const resultCard = $derived(item.relatedTo
    ? { ...thumbnailCard, subtitle: `Related to ${item.relatedTo.title}` }
    : thumbnailCard);
</script>

<EntityThumbnail
  card={resultCard}
  hoverPreviewsEnabled={false}
  artworkReactive={false}
  {highlighted}
/>
