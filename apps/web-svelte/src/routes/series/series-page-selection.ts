export function toggleSelectedId(selectedIds: ReadonlySet<string>, id: string) {
  const next = new Set(selectedIds);
  if (next.has(id)) next.delete(id);
  else next.add(id);
  return next;
}

export function getSelectionState(
  selectedIds: ReadonlySet<string>,
  visibleIds: string[],
) {
  return {
    visibleCount: visibleIds.length,
    allVisibleSelected:
      visibleIds.length > 0 && visibleIds.every((id) => selectedIds.has(id)),
  };
}

export function selectAllVisibleIds(
  selectedIds: ReadonlySet<string>,
  visibleIds: string[],
  allVisibleSelected = getSelectionState(selectedIds, visibleIds).allVisibleSelected,
) {
  return allVisibleSelected ? new Set<string>() : new Set(visibleIds);
}
