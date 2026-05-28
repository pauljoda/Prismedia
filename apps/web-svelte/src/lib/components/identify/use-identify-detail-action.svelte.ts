import { goto } from "$app/navigation";
import { Clock3, Loader2, ScanSearch } from "@lucide/svelte";
import {
  fetchIdentifyProviders,
  fetchOptionalIdentifyQueueItem,
  providerCanIdentifyKind,
} from "$lib/api/identify-client";
import type { IdentifyQueueItem } from "$lib/api/identify-types";
import type { EntityDetailActionButton } from "$lib/components/entities/EntityDetail.svelte";

/**
 * Creates a headless EntityDetail action for the Identify workflow.
 * EntityDetail owns rendering/styling; this helper only owns async provider and queue state.
 */
export function useIdentifyDetailAction(
  entityId: () => string | null | undefined,
  entityKind: () => string | null | undefined,
): { readonly action: EntityDetailActionButton | null } {
  let queuedItem: IdentifyQueueItem | null = $state(null);
  let hasReadyProvider = $state(true);
  let loading = $state(false);
  let lastLoadKey = "";

  const isQueued = $derived.by(() => queuedItem !== null && isActiveQueueState(queuedItem.state));
  const disabled = $derived(loading || (!isQueued && !hasReadyProvider));
  const label = $derived(
    loading
      ? "Checking"
      : isQueued
        ? "Pending Review"
        : hasReadyProvider
          ? "Identify"
          : "No Provider",
  );
  const title = $derived(
    isQueued
      ? "Open pending Identify review"
      : hasReadyProvider
        ? "Queue Identify review"
        : `No enabled Identify provider supports ${entityKind() ?? "this entity"}`,
  );

  $effect(() => {
    const id = entityId();
    const kind = entityKind();
    if (!id) {
      queuedItem = null;
      loading = false;
      lastLoadKey = "";
      return;
    }

    const loadKey = `${id}:${kind ?? ""}`;
    if (loadKey === lastLoadKey) return;
    lastLoadKey = loadKey;
    loading = true;
    let cancelled = false;

    void loadStatus(id, kind).finally(() => {
      if (!cancelled) loading = false;
    });

    return () => {
      cancelled = true;
    };
  });

  const action = $derived.by((): EntityDetailActionButton | null => {
    const id = entityId();
    if (!id) return null;

    return {
      id: "identify",
      label,
      icon: loading ? Loader2 : isQueued ? Clock3 : ScanSearch,
      iconClass: loading ? "h-3.5 w-3.5 animate-spin" : "h-3.5 w-3.5",
      title,
      ariaLabel: label,
      disabled,
      active: isQueued,
      onClick: () => navigate(id),
    };
  });

  async function loadStatus(id: string, kind: string | null | undefined) {
    const [queueItem, providers] = await Promise.all([
      fetchOptionalIdentifyQueueItem(id).catch(() => null),
      kind ? fetchIdentifyProviders(kind).catch(() => []) : Promise.resolve(null),
    ]);
    queuedItem = queueItem;
    hasReadyProvider = kind
      ? (providers ?? []).some((provider) => providerCanIdentifyKind(provider, kind))
      : true;
  }

  function navigate(id: string) {
    if (disabled) return;
    const params = new URLSearchParams({ returnId: id });
    if (isQueued) params.set("queued", "1");
    void goto(`/identify/${id}?${params.toString()}`);
  }

  return {
    get action() {
      return action;
    },
  };
}

function isActiveQueueState(state: IdentifyQueueItem["state"]): boolean {
  return state !== "done" && state !== "deleted";
}
