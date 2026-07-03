import { Search } from "@lucide/svelte";
import type { AcquisitionDetail, EntityCapability } from "$lib/api/generated/model";
import { firstProviderQualifiedId, isWanted } from "$lib/api/capabilities";
import { fetchAcquisitionForEntity } from "$lib/api/acquisitions";
import { commitEntityRequest } from "$lib/api/requests";
import type { EntityDetailActionButton } from "$lib/components/entities/EntityDetail.svelte";

/**
 * Creates the wanted-placeholder request surface every entity page shares: the entity's acquisition
 * (resolved by entity id, driving the inline <c>AcquisitionPanel</c>) and a headless "Search for
 * release" hero action for a phantom that has no acquisition yet. Committing goes through the single
 * server-side entity request path — the server resolves provider identity or falls back to the entity
 * graph — so a wanted book, movie, album, season, or episode is requested from its own page the exact
 * same way. Pages pass a reload callback so their surrounding state refreshes once the request starts.
 */
export function useWantedRequest(
  entityId: () => string | null | undefined,
  capabilities: () => EntityCapability[] | undefined,
  onRequested?: () => void | Promise<void>,
): {
  readonly acquisition: AcquisitionDetail | null;
  readonly wanted: boolean;
  readonly action: EntityDetailActionButton | null;
  refresh(): Promise<void>;
} {
  let acquisition = $state<AcquisitionDetail | null>(null);
  let busy = $state(false);
  let lastLoadedId = "";

  const wanted = $derived.by(() => {
    const caps = capabilities();
    return !!caps && isWanted(caps);
  });

  async function refresh(): Promise<void> {
    const id = entityId();
    acquisition = id ? await fetchAcquisitionForEntity(id).catch(() => null) : null;
  }

  $effect(() => {
    const id = entityId();
    if (!id) {
      acquisition = null;
      lastLoadedId = "";
      return;
    }

    if (id === lastLoadedId) return;
    lastLoadedId = id;
    void refresh();
  });

  /** Requests this phantom: starts its auto-grabbing, monitored acquisition and refreshes the page. */
  async function searchForRelease() {
    const id = entityId();
    if (!id || busy) return;
    busy = true;
    try {
      await commitEntityRequest(id);
      await refresh();
      await onRequested?.();
    } catch {
      // best-effort; the page reflects the last known state
    } finally {
      busy = false;
    }
  }

  const action = $derived.by((): EntityDetailActionButton | null => {
    // Only a phantom with no acquisition in flight offers the search; the entity needs a provider
    // identity (request-created phantoms always carry one) for the server to resolve or fall back on.
    const caps = capabilities();
    if (!entityId() || !wanted || acquisition !== null || !caps || !firstProviderQualifiedId(caps)) {
      return null;
    }

    return {
      id: "search-release",
      label: busy ? "Searching…" : "Search for release",
      icon: Search,
      variant: "primary",
      onClick: () => void searchForRelease(),
      disabled: busy,
    };
  });

  return {
    get acquisition() {
      return acquisition;
    },
    get wanted() {
      return wanted;
    },
    get action() {
      return action;
    },
    refresh,
  };
}
