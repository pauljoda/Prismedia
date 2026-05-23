// See https://svelte.dev/docs/kit/types#app.d.ts
// for information about these interfaces
import "vidstack/svelte";
import type { NsfwMode } from "$lib/nsfw/cookie";

declare global {
  namespace App {
    // interface Error {}
    // interface Locals {}
    interface PageData {
      initialCollapsed?: boolean;
      hasNsfwModeCookie?: boolean;
      initialNsfwMode?: NsfwMode;
      lanAutoEnable?: boolean;
    }
    // interface PageState {}
    // interface Platform {}
  }
}

export {};
