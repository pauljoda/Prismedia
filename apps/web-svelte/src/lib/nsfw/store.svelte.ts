import { browser } from "$app/environment";
import { invalidateAll } from "$app/navigation";
import { createContext } from "$lib/utils/context";
import { writeCookie as setCookie } from "$lib/utils/cookie";
import { isModShiftZ } from "./hotkey";
import { type NsfwMode } from "./cookie";

const COOKIE_NAME = "prismedia-nsfw-mode";
const ctx = createContext<NsfwStore>("Nsfw");

function writeNsfwCookie(mode: NsfwMode) {
  setCookie(COOKIE_NAME, mode);
}

/**
 * Tell SvelteKit to re-run every `+page.server.ts` / `+layout.server.ts`
 * loader. The NSFW cookie is read by those loaders to build API
 * query-strings, so any mode change must
 * refresh the data the API returned with the old value. Without this,
 * grids keep displaying the pre-toggle filtering until a hard reload.
 */
function refetchServerData() {
  if (!browser) return;
  void invalidateAll();
}

export class NsfwStore {
  mode = $state<NsfwMode>("off");
  /** True once the async LAN check has completed (or was skipped). */
  initialized = $state(false);
  private hasAutoEnabled = false;
  private keydownAttached = false;

  constructor(opts: { initialMode: NsfwMode; lanAutoEnable: boolean; hasExplicitMode?: boolean }) {
    this.mode = opts.initialMode;

    const hasExplicitMode = opts.hasExplicitMode ?? true;
    const shouldProbeLan = !hasExplicitMode && opts.lanAutoEnable && opts.initialMode !== "show";
    this.initialized = !shouldProbeLan;

    if (!browser) return;

    if (!this.initialized && !this.hasAutoEnabled) {
      this.hasAutoEnabled = true;
      // Defer the LAN probe until after the app has mounted on the client.
      queueMicrotask(() => {
        void this.detectLanAutoEnable();
      });
    }

    // Global keydown for ⌘⇧Z / Ctrl+Shift+Z
    $effect.root(() => {
      if (this.keydownAttached) return;
      this.keydownAttached = true;
      const handler = (e: KeyboardEvent) => {
        if (!this.initialized) return;
        if (!isModShiftZ(e)) return;
        e.preventDefault();
        e.stopPropagation();
        this.toggleShowOff();
      };
      window.addEventListener("keydown", handler, true);
      return () => {
        window.removeEventListener("keydown", handler, true);
        this.keydownAttached = false;
      };
    });
  }

  private async detectLanAutoEnable() {
    try {
      const response = await fetch("/api/client-info");
      const data = (await response.json()) as { isLan?: boolean };
      if (data.isLan) {
        this.setMode("show");
      }
    } catch {
      // Non-fatal; the UI should still initialize even if client info fails.
    } finally {
      this.initialized = true;
    }
  }

  setMode(next: NsfwMode) {
    if (this.mode === next) return;
    this.mode = next;
    writeNsfwCookie(next);
    refetchServerData();
  }

  toggleShowOff() {
    const next = this.mode === "show" ? "off" : "show";
    this.setMode(next);
  }
}

export function provideNsfw(
  getOpts: () => { initialMode: NsfwMode; lanAutoEnable: boolean; hasExplicitMode?: boolean },
) {
  return ctx.provide(new NsfwStore(getOpts()));
}

export const useNsfw = ctx.use;
