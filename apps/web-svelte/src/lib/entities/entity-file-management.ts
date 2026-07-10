/** Minimal acquisition state needed to retire a server-replaced acquisition before reloading a detail. */
export interface RevertedEntityAcquisitionState {
  clearAcquisition(): void;
  refresh(): Promise<void>;
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
