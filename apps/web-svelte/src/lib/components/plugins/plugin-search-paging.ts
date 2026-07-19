/** Initial and incremental candidate counts shared by Request and manual Identify searches. */
export const PLUGIN_SEARCH_PAGE_SIZE = 25;
export const PLUGIN_SEARCH_MAX_LIMIT = 100;

/** Returns the next cumulative provider result limit. */
export function nextPluginSearchLimit(current: number): number {
  return Math.min(PLUGIN_SEARCH_MAX_LIMIT, current + PLUGIN_SEARCH_PAGE_SIZE);
}
