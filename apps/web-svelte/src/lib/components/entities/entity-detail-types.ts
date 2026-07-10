import type { LucideIcon } from "@lucide/svelte";
import type { Snippet } from "svelte";
import type { CreditRoleCode, EntityFileRoleCode } from "$lib/entities/entity-codes";
import type { EntityDetailCard } from "$lib/entities/entity-detail";
import type {
  EntityMetadataPatch,
  EntityMetadataUpdateRequest,
} from "$lib/entities/entity-detail-edit";

export type EntityDetailPosterSize = "none" | "small" | "medium" | "large";

export interface EntityDetailTab {
  id: string;
  label: string;
  count?: number;
  icon?: LucideIcon;
  sections: string[];
  layout?: "stack" | "grid";
}

export type EntityDetailActionVariant = "default" | "primary" | "danger";

export interface EntityDetailActionButton {
  id: string;
  label: string;
  icon: LucideIcon;
  onClick?: () => void | Promise<void>;
  href?: string;
  title?: string;
  ariaLabel?: string;
  disabled?: boolean;
  /**
   * Hover/focus flyout shown for a disabled-looking action that stays clickable.
   * Without it, `disabled` renders a truly inert button.
   */
  disabledHint?: string;
  active?: boolean;
  hidden?: boolean;
  variant?: EntityDetailActionVariant;
  iconClass?: string;
  iconFill?: string;
}

export interface EntityDetailSection {
  id: string;
  label?: string;
  count?: number;
  icon?: LucideIcon;
  editable?: boolean;
  hidden?: boolean;
}

export interface EntityDetailProps {
  card: EntityDetailCard;
  onRatingChange?: (value: number | null) => void;
  onFavoriteToggle?: () => void;
  onOrganizedToggle?: () => void;
  peopleLabel?: string;
  /** Credit role pre-selected when adding people in the credits editor. */
  defaultCreditRole?: CreditRoleCode;
  posterSize?: EntityDetailPosterSize;
  ratingBusy?: boolean;
  showHero?: boolean;
  /**
   * Renders the favorite/NSFW/organized badge cluster in the hero. Off for
   * surfaces presenting external (non-library) items where those library
   * flags have no meaning, such as a read-only external preview.
   */
  showFlagActions?: boolean;
  tabs?: EntityDetailTab[];
  /** Built-in lower metadata sections used when this route does not provide tabs. */
  standaloneMetadataSectionIds?: string[];
  onMetadataSave?: (request: EntityMetadataUpdateRequest) => void | Promise<void>;
  onImageAssetUpload?: (role: EntityFileRoleCode, file: File) => void | Promise<void>;
  onImageAssetClear?: (role: EntityFileRoleCode) => void | Promise<void>;
  /** Route-provided sections that can be assigned to any tab. */
  sections?: EntityDetailSection[];
  /** Inline metadata rendered below the title (e.g. studio link, date, count). */
  heroMeta?: Snippet;
  /** Badge row rendered below the rating stars (e.g. Season 1, Episode 2). */
  heroBadges?: Snippet;
  /** Action buttons rendered in the right-aligned actions group. */
  actionButtons?: EntityDetailActionButton[];
  /** Content rendered between the detail body and the metadata sections (e.g. studio, credits). */
  afterBody?: Snippet;
  /** Extra metadata sections appended inside the lower metadata area. */
  extraSections?: Snippet;
  /** Custom content for route-provided sections. Core section IDs render built-in detail content. */
  sectionContent?: Snippet<[EntityDetailSection]>;
}

export type { EntityMetadataPatch, EntityMetadataUpdateRequest };
