/** Minimal acquisition state needed to retire a server-replaced acquisition before reloading a detail. */
export interface RevertedEntityAcquisitionState {
  clearAcquisition(): void;
  refresh(): Promise<void>;
}

/** Route follow-ups used by the shared Acquisition surface after managed file deletion settles. */
export interface EntityFileManagementCallbacks {
  /** Navigate away after the Entity and its subtree were fully removed. */
  onDeleted: () => void | Promise<void>;
  /** Refresh in place after monitoring kept the Entity as a Wanted placeholder. */
  onReverted: () => void | Promise<void>;
}

/**
 * Reconciles page-owned acquisition state after managed file deletion reverted an Entity to Wanted.
 * The old Imported acquisition id is removed before the replacement search is fetched, then the
 * owning route reloads the Entity document in place.
 */
export async function refreshAfterManagedFileRevert(
  acquisition: RevertedEntityAcquisitionState,
  reloadEntity: () => void | Promise<void>,
): Promise<void> {
  acquisition.clearAcquisition();
  await acquisition.refresh();
  await reloadEntity();
}
