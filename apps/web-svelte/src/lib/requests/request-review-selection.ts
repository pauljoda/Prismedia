import type {
  RequestReviewResponse,
  RequestReviewTarget,
} from "$lib/api/generated/model";
import { isRelationshipKind } from "$lib/components/identify-review";
import type { MonitorPresetChild } from "$lib/requests/monitor-presets";
import { requestKindInfo } from "$lib/requests/request-helpers";

/** The only two legal commit shapes exposed by the reviewed-request API. */
export type RequestReviewSelectionMode = "root" | "direct-children";

/**
 * Selection data derived exclusively from the canonical proposal and server-owned target map.
 * A proposal may contain structural metadata that is not independently requestable (album tracks,
 * for example), so the presence of children alone must never turn a leaf request into a fan-out.
 */
export interface RequestReviewSelectionModel {
  mode: RequestReviewSelectionMode;
  selectableIds: string[];
  initialRootSelection: string[];
  presetChildren: MonitorPresetChild[];
}

/**
 * Builds the route's selection model. Only targets that are also direct structural proposal children
 * become child selectors; descendant targets and relationship metadata are deliberately excluded.
 */
export function deriveRequestReviewSelection(review: RequestReviewResponse): RequestReviewSelectionModel {
  const seen = new Set<string>();
  const directChildren = review.proposal.children.filter((child) => {
    if (isRelationshipKind(child.targetKind) || seen.has(child.proposalId)) return false;
    seen.add(child.proposalId);
    return true;
  });
  const targetsByProposalId = new Map(review.targets.map((target) => [target.proposalId, target]));
  const directTargets = directChildren
    .map((child) => targetsByProposalId.get(child.proposalId))
    .filter((target): target is RequestReviewTarget => target !== undefined);
  const strategy = requestKindInfo(review.kind)?.reviewSelection ?? "root";
  const mode: RequestReviewSelectionMode = strategy === "direct-children"
    || strategy === "direct-children-when-present" && directChildren.length > 0
    ? "direct-children"
    : "root";
  const selectableIds = directTargets
    .filter((target) => target.requestable)
    .map((target) => target.proposalId);
  const rootTarget = targetsByProposalId.get(review.proposal.proposalId);

  return {
    mode,
    selectableIds,
    initialRootSelection:
      mode === "root" && rootTarget?.requestable ? [review.proposal.proposalId] : [],
    presetChildren: directTargets.map((target) => ({
      id: target.proposalId,
      number: numericValue(target.position),
      requestable: target.requestable,
    })),
  };
}

/** Finds the server target represented by a commit outcome without splitting its opaque value on colons. */
export function requestReviewTargetForExternalId(
  review: RequestReviewResponse,
  externalId: string,
): RequestReviewTarget | null {
  return review.targets.find(
    (target) => qualifiedIdentity(target) === externalId,
  ) ?? null;
}

function qualifiedIdentity(target: RequestReviewTarget): string {
  return `${target.externalIdentity.namespace}:${target.externalIdentity.value}`;
}

function numericValue(value: number | string | null | undefined): number | null {
  const parsed = typeof value === "string" ? Number(value) : value;
  return typeof parsed === "number" && Number.isFinite(parsed) ? parsed : null;
}
