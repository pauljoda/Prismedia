<script lang="ts">
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import type { ArtworkPalette } from "$lib/entities/artwork-palette";
  import { resolveEntityThumbnailHref, toAspectRatioValue } from "$lib/entities/entity-thumbnail";
  import EntityThumbnailArtwork from "./EntityThumbnailArtwork.svelte";
  import EntityThumbnailInfo from "./EntityThumbnailInfo.svelte";
  import type { EntityThumbnailProps } from "./entity-thumbnail-props";

  let {
    artworkReactive = true,
    card,
    density = "default",
    highlighted = false,
    imageFetchPriority = "low",
    imageLoading = "lazy",
    layout = "grid",
    linkable = true,
    linkTarget,
    mediaOnly = false,
    hoverPreviewsEnabled = true,
    hoverPreviewSuppressed,
    interactive = true,
    onActivate,
    onArtworkLoad,
    onSelectedChange,
    selectable = false,
    selectMode = false,
    selected = false,
    showWantedBadge = true,
    subtitleContent,
    titleAlign = "left",
    titleSize = "default",
  }: EntityThumbnailProps = $props();

  let artworkPaletteState = $state<{ entityId: string; palette: ArtworkPalette } | null>(null);
  let focusActive = $state(false);
  let isHovering = $state(false);
  let suppressNextActivation = false;

  const entityAccent = $derived(entityAccentForKind(card.entity.kind));
  const artworkPalette = $derived(
    artworkPaletteState?.entityId === card.entity.id ? artworkPaletteState.palette : null,
  );
  const activeAccent = $derived(artworkPalette ?? entityAccent);
  const imageOnly = $derived(mediaOnly);
  const aspectRatio = $derived(toAspectRatioValue(card.aspectRatio));
  const href = $derived(interactive && linkable ? resolveEntityThumbnailHref(card) : undefined);
  const inSelectMode = $derived(selectMode && selectable);
  const effectiveHref = $derived(inSelectMode ? undefined : href);
  const selectionRole = $derived(
    !interactive
      ? undefined
      : (onActivate && !effectiveHref)
        ? "button"
        : inSelectMode || (!href && selectable) ? "checkbox" : href ? undefined : "group",
  );
  const selectionTabIndex = $derived(interactive && !effectiveHref ? 0 : undefined);

  function handleArtworkPalette(entityId: string, palette: ArtworkPalette) {
    if (artworkPaletteState?.entityId !== entityId) artworkPaletteState = { entityId, palette };
  }
  function toggleSurfaceSelection() {
    if (!selectable || (!inSelectMode && href)) return;
    onSelectedChange?.(!selected);
  }
  function handleSurfaceClick(event: MouseEvent) {
    if (!interactive) return;
    if (suppressNextActivation) {
      suppressNextActivation = false;
      event.preventDefault();
      event.stopPropagation();
      return;
    }
    if (onActivate && !effectiveHref) {
      onActivate(card);
      return;
    }
    toggleSurfaceSelection();
  }
  function handleSurfaceKeydown(event: KeyboardEvent) {
    if (!interactive || (event.key !== "Enter" && event.key !== " ")) return;
    if (onActivate && !effectiveHref) {
      event.preventDefault();
      onActivate(card);
      return;
    }
    if (!selectable || (!inSelectMode && href)) return;
    event.preventDefault();
    toggleSurfaceSelection();
  }
</script>

<svelte:element
  this={effectiveHref ? "a" : "article"}
  href={effectiveHref || undefined}
  target={effectiveHref ? linkTarget : undefined}
  rel={effectiveHref && linkTarget === "_blank" ? "noopener noreferrer" : undefined}
  role={selectionRole}
  tabindex={selectionTabIndex}
  class="entity-thumbnail"
  class:is-hovering={isHovering}
  class:is-highlighted={highlighted}
  class:is-compact={density === "compact"}
  class:is-image-only={imageOnly}
  class:is-list={layout === "list"}
  class:is-selected={selected}
  class:is-select-mode={inSelectMode}
  class:is-static={!interactive}
  style:aspect-ratio={layout === "list" || !imageOnly ? undefined : aspectRatio}
  style:--entity-accent={activeAccent.primary}
  style:--entity-accent-secondary={activeAccent.secondary}
  aria-label={card.entity.title}
  aria-checked={interactive && !onActivate && (inSelectMode || (!href && selectable)) ? selected : undefined}
  onblur={() => (focusActive = false)}
  onclick={handleSurfaceClick}
  onfocus={() => (focusActive = true)}
  onkeydown={handleSurfaceKeydown}
>
  <EntityThumbnailArtwork
    {artworkReactive}
    {card}
    {density}
    {focusActive}
    {hoverPreviewsEnabled}
    {hoverPreviewSuppressed}
    {imageFetchPriority}
    {imageLoading}
    {layout}
    {mediaOnly}
    onActivationSuppressed={() => (suppressNextActivation = true)}
    {onArtworkLoad}
    onArtworkPalette={handleArtworkPalette}
    onHoverChange={(hovering) => (isHovering = hovering)}
    {onSelectedChange}
    {selectable}
    {selected}
    {showWantedBadge}
  />
  <EntityThumbnailInfo {card} {mediaOnly} {subtitleContent} {titleAlign} {titleSize} />
</svelte:element>

<style>
  .entity-thumbnail {
    position: relative;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    container-type: inline-size;
    min-width: 0;
    color: var(--color-text, #f4efe6);
    text-decoration: none;
  }
  .entity-thumbnail:not(.is-list):is(:hover, :focus-visible) :global(.media) { border-color: var(--color-border-default); box-shadow: var(--shadow-card-hover); transform: translateY(-1px); }
  .entity-thumbnail:not(.is-list).is-selected :global(.media) { border-color: var(--color-border-default); box-shadow: inset 2px 0 0 var(--entity-accent), var(--shadow-card-hover); }
  .entity-thumbnail:not(.is-list).is-highlighted :global(.media) { border-color: color-mix(in srgb, var(--entity-accent) 62%, var(--color-border-default)); box-shadow: inset 2px 0 0 var(--entity-accent), var(--shadow-card-hover); }
  .entity-thumbnail.is-static { pointer-events: none; }
  .entity-thumbnail.is-static:not(.is-list):is(:hover, :focus-visible) :global(.media) { border-color: var(--color-border-subtle, rgb(255 255 255 / 0.08)); box-shadow: var(--shadow-card); transform: none; }
  .entity-thumbnail.is-image-only { gap: 0; }
  .entity-thumbnail.is-list { flex-direction: row; gap: 0; inline-size: 100%; min-block-size: 5.25rem; overflow: hidden; border: 1px solid rgb(255 255 255 / 0.08); border-radius: 6px; background: rgb(12 12 13 / 0.92); box-shadow: inset 0 0 0 1px rgb(0 0 0 / 0.5), 0 2px 6px rgb(0 0 0 / 0.32); transition: transform 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)), border-color 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)), box-shadow 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)); }
  .entity-thumbnail.is-list:is(:hover, :focus-visible) { border-color: var(--color-border-default); box-shadow: var(--shadow-card-hover); transform: translateY(-1px); }
  .entity-thumbnail.is-list.is-selected { border-color: var(--color-border-default); box-shadow: inset 2px 0 0 var(--entity-accent), var(--shadow-card-hover); }
  .entity-thumbnail.is-list.is-highlighted { border-color: color-mix(in srgb, var(--entity-accent) 62%, var(--color-border-default)); background: color-mix(in srgb, var(--entity-accent) 10%, rgb(12 12 13 / 0.98)); box-shadow: inset 2px 0 0 var(--entity-accent), var(--shadow-card-hover); }
  .entity-thumbnail.is-list.is-compact { min-block-size: 3.25rem; }
  @media (prefers-reduced-motion: reduce) { .entity-thumbnail.is-list { transition: none; } .entity-thumbnail:is(:hover, :focus-visible), .entity-thumbnail:not(.is-list):is(:hover, :focus-visible) :global(.media) { transform: none; } }
</style>
