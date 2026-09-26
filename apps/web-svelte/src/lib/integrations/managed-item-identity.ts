import type { ManagedItemInput, ManagedLibraryItem } from "$lib/api/generated/model";

/** Matches a persisted identity pin while allowing the remote source to add identifiers later. */
export function isTrackedManagedItem(tracked: ManagedItemInput, item: ManagedLibraryItem): boolean {
  if (tracked.entityKind !== item.entityKind || tracked.remoteId !== item.remoteId) return false;
  const expected = Object.entries(tracked.expectedExternalIds);
  return expected.length > 0 && expected.every(([key, value]) => item.externalIds[key] === value);
}
