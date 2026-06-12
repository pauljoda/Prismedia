import { goto } from "$app/navigation";
import { Clock3, ScanSearch } from "@lucide/svelte";
import {
  fetchIdentifyProviders,
  fetchOptionalIdentifyQueueItem,
  providerCanIdentifyKind,
  requestIdentifySearch,
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
  let hasReadyProvider = $state(false);
  let loading = $state(false);
  let lastLoadKey = "";
  let loadVersion = 0;

  const isQueued = $derived.by(() => queuedItem !== null && isActiveQueueState(queuedItem.state));
  const label = $derived(isQueued ? "Pending Review" : "Identify");
  const title = $derived(
    isQueued
      ? "Open pending Identify review"
      : "Queue Identify review",
  );

  $effect(() => {
    const id = entityId();
    const kind = entityKind();
    if (!id) {
      queuedItem = null;
      hasReadyProvider = false;
      loading = false;
      lastLoadKey = "";
      loadVersion += 1;
      return;
    }

    const loadKey = `${id}:${kind ?? ""}`;
    if (loadKey === lastLoadKey) return;
    lastLoadKey = loadKey;
    void loadStatus(id, kind, true);

    return () => undefined;
  });

  $effect(() => {
    const id = entityId();
    if (!id || typeof window === "undefined") return;

    const refresh = () => {
      const currentId = entityId();
      if (!currentId) return;
      void loadStatus(currentId, entityKind(), false);
    };
    const refreshWhenVisible = () => {
      if (document.visibilityState === "visible") refresh();
    };

    window.addEventListener("focus", refresh);
    document.addEventListener("visibilitychange", refreshWhenVisible);
    return () => {
      window.removeEventListener("focus", refresh);
      document.removeEventListener("visibilitychange", refreshWhenVisible);
    };
  });

  const action = $derived.by((): EntityDetailActionButton | null => {
    const id = entityId();
    if (!id) return null;

    // The button always renders so the hero action row never reflows.
    if (loading) {
      return {
        id: "identify",
        label: "Identify",
        icon: ScanSearch,
        iconClass: "h-3.5 w-3.5",
        title: "Checking identify providers…",
        ariaLabel: "Identify (checking providers)",
        disabled: true,
      };
    }

    if (!isQueued && !hasReadyProvider) {
      return {
        id: "identify",
        label: "Identify",
        icon: ScanSearch,
        iconClass: "h-3.5 w-3.5",
        ariaLabel: "Identify (no compatible plugin installed)",
        disabled: true,
        disabledHint: "No compatible Identify plugin for this media type. Click to open Plugins and install one.",
        onClick: () => void goto("/plugins"),
      };
    }

    return {
      id: "identify",
      label,
      icon: isQueued ? Clock3 : ScanSearch,
      iconClass: "h-3.5 w-3.5",
      title,
      ariaLabel: label,
      active: isQueued,
      onClick: () => void navigate(id),
    };
  });

  async function loadStatus(id: string, kind: string | null | undefined, showLoading: boolean) {
    const currentVersion = ++loadVersion;
    if (showLoading) loading = true;

    const [queueItem, providers] = await Promise.all([
      fetchOptionalIdentifyQueueItem(id).catch(() => null),
      kind ? fetchIdentifyProviders(kind).catch(() => []) : Promise.resolve(null),
    ]);
    if (currentVersion !== loadVersion) {
      if (showLoading) loading = false;
      return;
    }

    queuedItem = queueItem;
    hasReadyProvider = kind
      ? (providers ?? []).some((provider) => providerCanIdentifyKind(provider, kind))
      : false;
    if (showLoading) loading = false;
  }

  async function navigate(id: string) {
    if (!isQueued && !hasReadyProvider) return;
    // A fresh identify requests the search up front (the server walks enabled providers);
    // the review page then just renders the item's queued → searching → result states.
    if (!isQueued) {
      await requestIdentifySearch(id, null).catch(() => undefined);
    }

    const params = new URLSearchParams({ returnId: id });
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
