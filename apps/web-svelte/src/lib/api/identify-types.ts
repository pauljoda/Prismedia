import type { EntityKind } from "$lib/api/generated/model";
import type { EntityMetadataFlagsPatch } from "$lib/api/entity-mutations";

export interface PluginEntitySupport {
  entityKind: string;
  actions: string[];
}

export interface PluginAuthField {
  key: string;
  label: string;
  required: boolean;
  url?: string | null;
}

export interface PluginProvider {
  id: string;
  name: string;
  version: string;
  installed: boolean;
  enabled: boolean;
  isNsfw: boolean;
  supports: PluginEntitySupport[];
  auth: PluginAuthField[];
  missingAuthKeys: string[];
  updateAvailable?: boolean;
  availableVersion?: string | null;
}

export interface IdentifyQuery {
  title?: string | null;
  url?: string | null;
  externalIds?: Record<string, string> | null;
  requireChoice?: boolean | null;
}

export interface ImageCandidate {
  kind: string;
  url: string;
  source: string;
  rank?: number | null;
  language?: string | null;
  width?: number | null;
  height?: number | null;
}

export interface EntitySearchCandidate {
  externalIds: Record<string, string>;
  title: string;
  year?: number | null;
  overview?: string | null;
  posterUrl?: string | null;
  popularity?: number | null;
  candidateId?: string | null;
  source?: string | null;
  confidence?: number | null;
  matchReason?: string | null;
}

export interface CreditPatch {
  name: string;
  role: string;
  character?: string | null;
  sortOrder?: number | null;
}

export type { EntityMetadataFlagsPatch };

export interface EntityMetadataPatch {
  title?: string | null;
  description?: string | null;
  externalIds: Record<string, string>;
  urls: string[];
  tags: string[];
  studio?: string | null;
  credits: CreditPatch[];
  dates: Record<string, string>;
  stats: Record<string, number>;
  positions: Record<string, number>;
  classification?: string | null;
  flags?: EntityMetadataFlagsPatch | null;
}

export interface EntityMetadataProposal {
  proposalId: string;
  provider: string;
  targetKind: string;
  confidence?: number | null;
  matchReason?: string | null;
  patch: EntityMetadataPatch;
  images: ImageCandidate[];
  children: EntityMetadataProposal[];
  relationships: EntityMetadataProposal[];
  candidates: EntitySearchCandidate[];
  targetEntityId?: string | null;
}

export interface IdentifyBulkResult {
  entityId: string;
  response: {
    ok: boolean;
    result?: EntityMetadataProposal | null;
    error?: string | null;
  };
}

export interface IdentifyBulkSession {
  id: string;
  provider: string;
  entityIds: string[];
  results: IdentifyBulkResult[];
  status: "running" | "completed" | string;
  createdAt: string;
}

export type IdentifyQueueState = "search" | "proposal" | "done" | "deleted" | "error";

export interface IdentifyApplyProgress {
  id: string;
  entityId: string;
  state: "running" | "succeeded" | "failed" | string;
  currentIndex: number;
  total: number;
  currentKind?: EntityKind | null;
  currentTitle?: string | null;
  currentPath: string[];
  error?: string | null;
  updatedAt: string;
}

export interface IdentifyQueueItem {
  id: string;
  entityId: string;
  entityKind: EntityKind;
  title: string;
  isNsfw: boolean;
  state: IdentifyQueueState;
  provider?: string | null;
  action: string;
  query?: IdentifyQuery | null;
  candidates: EntitySearchCandidate[];
  proposal?: EntityMetadataProposal | null;
  error?: string | null;
  /** True while a background cascade is still streaming this item's child tree into the proposal. */
  cascadeRunning: boolean;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
}
