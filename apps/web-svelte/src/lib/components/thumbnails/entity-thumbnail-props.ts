import type { Snippet } from "svelte";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

export type EntityThumbnailDensity = "default" | "compact";
export type EntityThumbnailImageFetchPriority = "auto" | "high" | "low";
export type EntityThumbnailImageLoading = "eager" | "lazy";
export type EntityThumbnailTitleAlign = "left" | "center" | "right";
export type EntityThumbnailTitleSize = "default" | "compact";

/** Shared presentation contract for every Entity thumbnail surface. */
export interface EntityThumbnailProps {
  card: EntityThumbnailCard;
  density?: EntityThumbnailDensity;
  /** Applies host-owned active-result emphasis without changing selection state. */
  highlighted?: boolean;
  imageFetchPriority?: EntityThumbnailImageFetchPriority;
  imageLoading?: EntityThumbnailImageLoading;
  layout?: "grid" | "list";
  linkable?: boolean;
  linkTarget?: "_self" | "_blank" | "_parent" | "_top";
  mediaOnly?: boolean;
  hoverPreviewsEnabled?: boolean;
  hoverPreviewSuppressed?: () => boolean;
  interactive?: boolean;
  onActivate?: (card: EntityThumbnailCard) => void;
  onArtworkLoad?: (image: HTMLImageElement) => void;
  onSelectedChange?: (selected: boolean) => void;
  selectable?: boolean;
  selectMode?: boolean;
  selected?: boolean;
  /** Show the wanted/status badge on the artwork. Off where a host surface renders status itself. */
  showWantedBadge?: boolean;
  subtitleContent?: Snippet<[EntityThumbnailCard]>;
  titleAlign?: EntityThumbnailTitleAlign;
  titleSize?: EntityThumbnailTitleSize;
}
