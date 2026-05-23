import { getContext, setContext } from "svelte";

const KEY = Symbol("page-snapshots");

interface ScrollSnapshot {
  top: number;
  left: number;
}

export interface AppPageSnapshot {
  scroll: ScrollSnapshot;
  surfaces: Record<string, unknown>;
}

interface PageSnapshotsOptions {
  captureScroll: () => ScrollSnapshot;
  restoreScroll: (snapshot: ScrollSnapshot) => void;
}

export interface SurfaceSnapshotApi<T = unknown> {
  capture: () => T;
  restore: (snapshot: T) => void;
}

export class PageSnapshotsStore {
  private surfaces = new Map<string, SurfaceSnapshotApi>();
  private pendingSurfaces = new Map<string, unknown>();

  constructor(private options: PageSnapshotsOptions) {}

  registerSurface<T>(surfaceId: string, api: SurfaceSnapshotApi<T>): () => void {
    this.surfaces.set(surfaceId, api as SurfaceSnapshotApi);

    const pending = this.pendingSurfaces.get(surfaceId);
    if (pending) {
      api.restore(pending as T);
      this.pendingSurfaces.delete(surfaceId);
    }

    return () => {
      if (this.surfaces.get(surfaceId) === (api as SurfaceSnapshotApi)) {
        this.surfaces.delete(surfaceId);
      }
    };
  }

  capture(): AppPageSnapshot {
    return {
      scroll: this.options.captureScroll(),
      surfaces: Object.fromEntries(
        [...this.surfaces.entries()].map(([surfaceId, api]) => [surfaceId, api.capture()]),
      ),
    };
  }

  restore(snapshot: AppPageSnapshot) {
    this.pendingSurfaces = new Map(Object.entries(snapshot.surfaces));

    for (const [surfaceId, api] of this.surfaces.entries()) {
      const pending = this.pendingSurfaces.get(surfaceId);
      if (!pending) continue;
      api.restore(pending);
      this.pendingSurfaces.delete(surfaceId);
    }

    this.options.restoreScroll(snapshot.scroll);
  }
}

const FALLBACK = new PageSnapshotsStore({
  captureScroll: () => ({ top: 0, left: 0 }),
  restoreScroll: () => {},
});

export function providePageSnapshots(options: PageSnapshotsOptions) {
  const store = new PageSnapshotsStore(options);
  setContext(KEY, store);
  return store;
}

export function usePageSnapshots(): PageSnapshotsStore {
  const ctx = getContext<PageSnapshotsStore | undefined>(KEY);
  return ctx ?? FALLBACK;
}
