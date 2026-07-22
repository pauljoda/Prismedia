/**
 * Collapses overlapping refresh requests into one active pass plus one trailing pass.
 *
 * Polling surfaces can be refreshed by timers, focus, visibility, and user actions at the same
 * moment. Keeping one drain promise prevents slow requests from accumulating while still ensuring
 * that a refresh requested during an active pass is not lost.
 */
export function createSerializedRefresh(refresh: () => Promise<void>): () => Promise<void> {
  let active: Promise<void> | null = null;
  let requested = false;

  const request = (): Promise<void> => {
    requested = true;
    if (active) return active;

    // Start on the next microtask so active is assigned before refresh reaches its first await.
    active = Promise.resolve().then(async () => {
      try {
        while (requested) {
          requested = false;
          await refresh();
        }
      } finally {
        active = null;
        if (requested) void request();
      }
    });

    return active;
  };

  return request;
}
