import type { LayoutLoad } from "./$types";
import { readSidebarCookie } from "$lib/stores/app-chrome.svelte";

export const ssr = false;

export const load: LayoutLoad = async () => {
  const initialCollapsed = readSidebarCookie();
  return { initialCollapsed };
};
