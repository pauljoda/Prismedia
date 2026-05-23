import { getContext, setContext } from "svelte";
import { browser } from "$app/environment";
import { isModK } from "../nsfw/hotkey";

const KEY = Symbol("search");

export class SearchStore {
  open = $state(false);
  private attached = false;

  constructor() {
    if (!browser) return;
    $effect.root(() => {
      if (this.attached) return;
      this.attached = true;
      const handler = (e: KeyboardEvent) => {
        if (isModK(e)) {
          e.preventDefault();
          e.stopPropagation();
          this.open = true;
          return;
        }
        if (e.key === "Escape" && this.open) {
          e.preventDefault();
          e.stopPropagation();
          this.open = false;
        }
      };
      window.addEventListener("keydown", handler, true);
      return () => {
        window.removeEventListener("keydown", handler, true);
        this.attached = false;
      };
    });
  }

  openPalette() {
    this.open = true;
  }

  closePalette() {
    this.open = false;
  }
}

export function provideSearch() {
  const store = new SearchStore();
  setContext(KEY, store);
  return store;
}

export function useSearch(): SearchStore {
  const ctx = getContext<SearchStore | undefined>(KEY);
  if (!ctx) throw new Error("useSearch must be used inside <SearchProvider>");
  return ctx;
}
