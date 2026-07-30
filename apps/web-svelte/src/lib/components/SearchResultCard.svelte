<script module lang="ts">
  export type SearchResultCardVariant = "grid" | "compact";
</script>

<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import { assetUrl } from "$lib/api/orval-fetch";
  import { buildHrefWithFrom } from "$lib/back-navigation";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";
  import type { SearchResultItem } from "$lib/search/models";
  import { SEARCH_KIND_CONFIG } from "./search-kind-config";

  interface Props {
    item: SearchResultItem;
    index?: number;
    variant?: SearchResultCardVariant;
    currentPath?: string;
    onSelect?: (href: string) => void;
    highlighted?: boolean;
  }

  let {
    item,
    index = 0,
    variant = "grid",
    currentPath,
    onSelect,
    highlighted = false,
  }: Props = $props();

  const href = $derived(buildHrefWithFrom(item.href, currentPath ?? ""));
  const label = $derived(SEARCH_KIND_CONFIG[item.kind]?.label ?? item.kind);
  const imageUrl = $derived(assetUrl(item.imagePath));
  const imageFit = $derived(item.kind === ENTITY_KIND.movie || item.kind === ENTITY_KIND.video || item.kind === ENTITY_KIND.gallery ? "cover" : "contain");
  const resultCard = $derived(
    entityReferenceToThumbnailCard(
      { id: item.id, kind: item.kind, title: item.title },
      {
        cover: imageUrl ? { src: imageUrl, alt: item.title } : null,
        fit: imageFit,
        href: variant === "grid" ? href : undefined,
      },
    ),
  );
  const resultLabel = $derived(item.matchType === "related" ? "Related" : label);
</script>

{#if variant === "compact"}
  <EntityThumbnail
    card={resultCard}
    layout="list"
    density="compact"
    linkable={false}
    hoverPreviewsEnabled={false}
    titleSize="compact"
    {highlighted}
    onActivate={() => onSelect?.(item.href)}
  >
    {#snippet subtitleContent()}
      <div class="flex min-w-0 items-center gap-2">
        {#if item.subtitle}
          <span class="min-w-0 flex-1 truncate font-mono text-[0.62rem] text-text-muted">{item.subtitle}</span>
        {/if}
        <span class={cn("tag-chip shrink-0 text-[0.6rem]", highlighted ? "tag-chip-accent" : "tag-chip-default")}>
          {resultLabel}
        </span>
      </div>
    {/snippet}
  </EntityThumbnail>
{:else}
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
{/if}
