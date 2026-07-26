import { IDENTIFY_APPLY_STATE } from "$lib/api/generated/codes";
import type { EntityMetadataProposal, IdentifyApplyProgress } from "$lib/api/identify-types";
import type { EntityCard } from "$lib/api/entities";

const MIN_APPLY_PROGRESS_VISIBLE_MS = 650;

export function createOperationId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

export function nowMs(): number {
  return globalThis.performance?.now?.() ?? Date.now();
}

export async function waitForMinimumApplyProgress(startedAt: number): Promise<void> {
  const remaining = MIN_APPLY_PROGRESS_VISIBLE_MS - (nowMs() - startedAt);
  if (remaining > 0) {
    await wait(remaining);
  }
}

export function wait(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function initialApplyProgress(
  id: string,
  entity: EntityCard,
  proposal: EntityMetadataProposal,
  selectedFields: string[],
): IdentifyApplyProgress {
  const title = proposalTitleForProgress(proposal) || entity.title;
  return {
    id,
    entityId: entity.id,
    state: IDENTIFY_APPLY_STATE.running,
    currentIndex: 0,
    total: countApplyProgressSteps(proposal, selectedFields),
    // Optimistic progress is for the root entity being applied, so its real kind
    // is the entity's own kind. (The proposal target vocabulary can carry
    // non-entity tokens like "video-episode", which this typed field must not.)
    currentKind: entity.kind,
    currentTitle: title,
    currentPath: [title],
    error: null,
    updatedAt: new Date().toISOString(),
  };
}

function countApplyProgressSteps(
  proposal: EntityMetadataProposal,
  selectedFields: string[],
): number {
  const selected = new Set(selectedFields.map((field) => field.toLowerCase()));
  let count = 1;
  if (selected.has("credits") || selected.has("studio") || selected.has("tags")) {
    count += relationshipProgressSteps(proposal);
  }

  count += structuralProgressChildren(proposal).reduce(
    (total, child) => total + countStructuralApplyProgressSteps(child),
    0,
  );
  return Math.max(count, 1);
}

function countStructuralApplyProgressSteps(proposal: EntityMetadataProposal): number {
  let count = 1;
  if (proposal.patch.credits.length > 0 || Boolean(proposal.patch.studio?.trim()) || proposal.patch.tags.length > 0) {
    count += relationshipProgressSteps(proposal);
  }

  count += structuralProgressChildren(proposal).reduce(
    (total, child) => total + countStructuralApplyProgressSteps(child),
    0,
  );
  return count;
}

function relationshipProgressSteps(proposal: EntityMetadataProposal): number {
  return new Set(
    (proposal.relationships ?? [])
      .filter((child) => isRelationshipProgressKind(child.targetKind))
      .map((child) => child.proposalId),
  ).size;
}

function structuralProgressChildren(proposal: EntityMetadataProposal): EntityMetadataProposal[] {
  return (proposal.children ?? []).filter((child) => !isRelationshipProgressKind(child.targetKind));
}

function isRelationshipProgressKind(kind: string): boolean {
  return kind === "person" || kind === "studio" || kind === "tag";
}

function proposalTitleForProgress(proposal: EntityMetadataProposal): string {
  return proposal.patch.title?.trim() ?? "";
}
