import type { IdentifyQueueItem } from "./identify-store.svelte";

interface QueueRejectActionState {
  current: IdentifyQueueItem | null;
  reviewSurfaceHasRejectFooter: boolean;
  isIdentifyingCurrent: boolean;
}

export function shouldShowRouteQueueRejectActions({
  current,
  reviewSurfaceHasRejectFooter,
  isIdentifyingCurrent,
}: QueueRejectActionState): boolean {
  return Boolean(current && !reviewSurfaceHasRejectFooter && !isIdentifyingCurrent);
}
