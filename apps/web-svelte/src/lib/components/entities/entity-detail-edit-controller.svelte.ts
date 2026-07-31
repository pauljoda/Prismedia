import type { EntityDetailCard } from "$lib/entities/entity-detail";
import {
  buildMetadataUpdate,
  createEmptyEntityDetailEditDraft,
  draftFromCard,
  serializeDraft,
  validateDraft,
  type EntityMetadataUpdateRequest,
} from "$lib/entities/entity-detail-edit";
import type { EntityDetailSection, EntityDetailTab } from "./entity-detail-types";

interface EntityDetailEditFlags {
  isFavorite: boolean;
  isNsfw: boolean;
  isOrganized: boolean;
}

export interface EntityDetailEditControllerOptions {
  card: () => EntityDetailCard;
  flags: () => EntityDetailEditFlags;
  hasTabs: () => boolean;
  activeTab: () => EntityDetailTab | null;
  activeTabSections: () => EntityDetailSection[];
  standaloneSections: () => EntityDetailSection[];
  ratingMax: () => number;
  save: () => ((request: EntityMetadataUpdateRequest) => void | Promise<void>) | undefined;
  activateTab: (tabId: string) => void;
  onStart?: () => void;
}

/**
 * Owns one EntityDetail metadata-edit session, including its draft, validation,
 * dirty-tab guard, persistence state, and user-facing save error.
 */
export class EntityDetailEditController {
  readonly draft = $state(createEmptyEntityDetailEditDraft());
  editingTabId = $state<string | null>(null);
  pendingTabId = $state<string | null>(null);
  saving = $state(false);
  error = $state<string | null>(null);

  private initialDraftSignature: string | null = null;

  constructor(private readonly options: EntityDetailEditControllerOptions) {}

  get isEditingActiveTab(): boolean {
    const activeTab = this.options.activeTab();
    return this.options.hasTabs()
      ? Boolean(activeTab && this.editingTabId === activeTab.id)
      : this.editingTabId === "__standalone__";
  }

  get sections(): EntityDetailSection[] {
    return this.options.hasTabs()
      ? this.options.activeTabSections()
      : this.options.standaloneSections();
  }

  get validationErrors(): string[] {
    return validateDraft(this.sections, this.draft, this.options.ratingMax());
  }

  get dirty(): boolean {
    return this.initialDraftSignature !== null
      && this.initialDraftSignature !== serializeDraft(this.draft);
  }

  get saveDisabled(): boolean {
    return !this.dirty || this.validationErrors.length > 0 || this.saving;
  }

  start = (tab?: EntityDetailTab): void => {
    const nextDraft = draftFromCard(this.options.card(), this.options.flags());
    Object.assign(this.draft, nextDraft);
    this.initialDraftSignature = serializeDraft(nextDraft);
    this.editingTabId = tab?.id ?? "__standalone__";
    this.error = null;
    this.options.onStart?.();
  };

  cancel = (): void => {
    this.editingTabId = null;
    this.initialDraftSignature = null;
    this.error = null;
  };

  requestTab = (tabId: string): void => {
    if (tabId === this.options.activeTab()?.id) return;
    if (this.dirty) {
      this.pendingTabId = tabId;
      return;
    }

    this.options.activateTab(tabId);
    this.cancel();
  };

  stayOnDirtyTab = (): void => {
    this.pendingTabId = null;
  };

  discardDirtyTab = (): void => {
    if (this.pendingTabId) this.options.activateTab(this.pendingTabId);
    this.pendingTabId = null;
    this.cancel();
  };

  save = async (): Promise<void> => {
    const persist = this.options.save();
    if (!persist || this.saveDisabled) return;

    this.saving = true;
    this.error = null;
    try {
      await persist(buildMetadataUpdate(this.sections, this.draft));
      this.cancel();
    } catch (error) {
      this.error = error instanceof Error ? error.message : String(error);
    } finally {
      this.saving = false;
    }
  };
}
