<script lang="ts">
  import { Badge, Command } from "@prismedia/ui-svelte";
  import { assetUrl } from "$lib/api/orval-fetch";
  import EntityThumbnail from "./thumbnails/EntityThumbnail.svelte";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";
  import type { SearchResultItem } from "$lib/search/models";
  import { SEARCH_KIND_CONFIG } from "./search-kind-config";

  let { item, onSelect }: { item: SearchResultItem; onSelect: (href: string) => void } = $props();
  const imageUrl = $derived(assetUrl(item.imagePath));
  const card = $derived(entityReferenceToThumbnailCard(
    { id: item.id, kind: item.kind, title: item.title },
    { cover: imageUrl ? { src: imageUrl, alt: item.title } : null },
  ));
  const subtitle = $derived(item.subtitle !== SEARCH_KIND_CONFIG[item.kind].label ? item.subtitle : null);
</script>

<Command.Item value={`${item.kind}:${item.id}`} showIndicator={false} onSelect={() => onSelect(item.href)} class="gap-3">
  <div class="w-10 shrink-0" aria-hidden="true">
    <EntityThumbnail {card} mediaOnly interactive={false} artworkReactive={false} hoverPreviewsEnabled={false} showWantedBadge={false} />
  </div>
  <span class="flex min-w-0 flex-1 flex-col gap-1">
    <span class="truncate font-medium text-foreground">{item.title}</span>
    {#if subtitle}<span class="truncate text-xs text-muted-foreground">{subtitle}</span>{/if}
  </span>
  {#if item.matchType === "related"}<Badge>Related</Badge>{/if}
</Command.Item>
