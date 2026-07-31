import { getImagesCapability } from "$lib/api/capabilities";
import { clearEntityImageAsset, uploadEntityImageAsset } from "$lib/api/entity-mutations";
import type { EntityDetailCard } from "$lib/entities/entity-detail";
import { ENTITY_FILE_ROLE, type EntityFileRoleCode } from "$lib/entities/entity-codes";
import type { EntityMetadataUpdateRequest } from "$lib/entities/entity-detail-edit";

interface LocalArtworkAsset {
  entityId: string;
  src: string | null;
}

export interface EntityDetailArtworkControllerOptions {
  card: () => EntityDetailCard;
  metadataSave: () => ((request: EntityMetadataUpdateRequest) => void | Promise<void>) | undefined;
  upload: () => ((role: EntityFileRoleCode, file: File) => void | Promise<void>) | undefined;
  clear: () => ((role: EntityFileRoleCode) => void | Promise<void>) | undefined;
}

/**
 * Owns editable EntityDetail artwork state, including optimistic local assets,
 * upload/clear requests, supported roles, busy state, and user-facing failures.
 */
export class EntityDetailArtworkController {
  error = $state<string | null>(null);
  busyRole = $state<EntityFileRoleCode | null>(null);

  private poster = $state<LocalArtworkAsset | null>(null);
  private header = $state<LocalArtworkAsset | null>(null);

  constructor(private readonly options: EntityDetailArtworkControllerOptions) {}

  get displayPoster(): EntityDetailCard["poster"] {
    const local = this.posterForCurrentEntity;
    return local ? (local.src ? { src: local.src, alt: "Poster" } : null) : this.options.card().poster;
  }

  get displayHeader(): EntityDetailCard["hero"] {
    const local = this.headerForCurrentEntity;
    return local ? (local.src ? { src: local.src, alt: "Header" } : null) : this.options.card().hero;
  }

  get canManage(): boolean {
    return Boolean(this.options.metadataSave() || this.options.upload() || this.options.clear());
  }

  supports(role: EntityFileRoleCode): boolean {
    const supportedKinds = getImagesCapability(this.options.card().entity.capabilities)?.supportedKinds ?? [];
    return supportedKinds.length === 0 || supportedKinds.includes(role);
  }

  isBusy(role: EntityFileRoleCode): boolean {
    return this.busyRole === role;
  }

  clearError = (): void => {
    this.error = null;
  };

  uploadAsset = async (role: EntityFileRoleCode, file: File): Promise<void> => {
    if (this.busyRole) return;
    this.busyRole = role;
    this.error = null;
    try {
      const upload = this.options.upload();
      await (upload
        ? upload(role, file)
        : uploadEntityImageAsset(this.options.card().entity.id, role, file));
      this.apply(role, URL.createObjectURL(file));
    } catch (error) {
      this.error = error instanceof Error ? error.message : String(error);
    } finally {
      this.busyRole = null;
    }
  };

  clearAsset = async (role: EntityFileRoleCode): Promise<void> => {
    if (this.busyRole) return;
    this.busyRole = role;
    this.error = null;
    try {
      const clear = this.options.clear();
      await (clear
        ? clear(role)
        : clearEntityImageAsset(this.options.card().entity.id, role));
      this.apply(role, null);
    } catch (error) {
      this.error = error instanceof Error ? error.message : String(error);
    } finally {
      this.busyRole = null;
    }
  };

  private get posterForCurrentEntity(): LocalArtworkAsset | null {
    return this.poster?.entityId === this.options.card().entity.id ? this.poster : null;
  }

  private get headerForCurrentEntity(): LocalArtworkAsset | null {
    return this.header?.entityId === this.options.card().entity.id ? this.header : null;
  }

  private apply(role: EntityFileRoleCode, src: string | null): void {
    const asset = { entityId: this.options.card().entity.id, src };
    if (role === ENTITY_FILE_ROLE.backdrop) {
      this.header = asset;
    } else {
      this.poster = asset;
    }
  }
}
