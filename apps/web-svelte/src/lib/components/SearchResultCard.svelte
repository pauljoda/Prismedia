<script module lang="ts">
  export type SearchResultCardVariant = "grid" | "compact";
</script>

<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import { assetUrl } from "$lib/api/orval-fetch";
  import { buildHrefWithFrom } from "$lib/back-navigation";
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
  const isTallGrid = $derived(item.kind === "video-series" || item.kind === "performer");
  const isRowGrid = $derived(
    item.kind === "studio" ||
      item.kind === "audio-library" ||
      item.kind === "audio-track",
  );
  const compactFrameClass = $derived.by(() => {
    if (item.kind === "video") return "h-8 w-12";
    if (item.kind === "video-series" || item.kind === "gallery") return "h-10 w-7";
    return "h-8 w-8";
  });
  const imageUrl = $derived(assetUrl(item.imagePath));
  const imageFit = $derived(item.kind === "video" || item.kind === "gallery" ? "cover" : "contain");
</script>

{#snippet Thumbnail(className: string)}
  <div class={cn("flex items-center justify-center overflow-hidden bg-surface-1", className)}>
    {#if imageUrl}
      <img src={imageUrl} alt="" class="h-full w-full" style:object-fit={imageFit} loading="lazy" />
    {:else}
      <span class="font-heading text-sm text-text-disabled">{item.title.slice(0, 1).toUpperCase()}</span>
    {/if}
  </div>
{/snippet}

{#if variant === "compact"}
  <button
    type="button"
    class={cn(
      "flex w-full items-center gap-3 px-4 py-2 text-left transition-colors duration-fast hover:bg-surface-2",
      highlighted && "bg-accent-950/45 ring-1 ring-inset ring-accent-500/45",
    )}
    onclick={() => onSelect?.(item.href)}
  >
    {@render Thumbnail(cn("shrink-0", compactFrameClass))}
    <div class="min-w-0 flex-1">
      <div class="truncate text-sm text-text-primary">{item.title}</div>
      {#if item.subtitle}
        <div class="truncate text-[0.68rem] text-text-muted">{item.subtitle}</div>
      {/if}
    </div>
    <span class={cn("tag-chip shrink-0 text-[0.6rem]", highlighted ? "tag-chip-accent" : "tag-chip-default")}>
      {item.matchType === "related" ? "Related" : label}
    </span>
  </button>
{:else if isRowGrid}
  <a
    {href}
    class={cn(
      "surface-card-sharp flex items-center gap-3 p-2 transition-colors duration-fast hover:border-border-accent group/card",
      highlighted && "border-border-accent bg-accent-950/30 shadow-[0_0_24px_rgba(242,194,106,0.12)]",
    )}
  >
    <div class="h-16 w-16 shrink-0">
      {@render Thumbnail("h-full w-full")}
    </div>
    <div class="min-w-0 flex-1">
      <div class="truncate text-sm text-text-primary">{item.title}</div>
      {#if item.subtitle}
        <div class="truncate text-[0.65rem] text-text-muted">{item.subtitle}</div>
      {/if}
    </div>
  </a>
{:else}
  <a
    {href}
    class={cn(
      "surface-card-sharp overflow-hidden transition-colors duration-fast hover:border-border-accent block",
      isTallGrid && "flex flex-col",
      highlighted && "border-border-accent bg-accent-950/30 shadow-[0_0_24px_rgba(242,194,106,0.12)]",
    )}
  >
    {@render Thumbnail(cn("w-full", item.kind === "video" ? "aspect-video" : isTallGrid ? "aspect-[2/3]" : "aspect-square"))}
    <div class={cn("space-y-1", item.kind === "image" ? "px-1.5 py-1" : "p-2.5")}>
      <h4
        class={cn(
          "truncate font-medium text-text-primary",
          item.kind === "image" ? "text-[0.62rem] text-text-muted" : "text-body",
        )}
      >
        {item.title}
      </h4>
      {#if item.subtitle && item.kind !== "image"}
        <div class="truncate text-[0.65rem] text-text-muted">{item.subtitle}</div>
      {/if}
    </div>
  </a>
{/if}
