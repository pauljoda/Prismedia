import { ENTITY_LIST_SORT } from "$lib/api/generated/codes";

/** Local grid ordering keys, including client-only sorts and the persisted legacy date key. */
export const ENTITY_GRID_SORT = {
  title: ENTITY_LIST_SORT.title,
  kind: "kind",
  rating: ENTITY_LIST_SORT.rating,
  position: "position",
  added: "added",
  random: ENTITY_LIST_SORT.random,
  references: ENTITY_LIST_SORT.references,
} as const;
export type EntityGridSort = (typeof ENTITY_GRID_SORT)[keyof typeof ENTITY_GRID_SORT];

/** Frontend-only layout choices; these are not server entity codes. */
export const ENTITY_GRID_VIEW_MODE = { grid: "grid", list: "list", feed: "feed" } as const;
export type EntityGridViewMode = (typeof ENTITY_GRID_VIEW_MODE)[keyof typeof ENTITY_GRID_VIEW_MODE];
