/**
 * Rune-based multi-select helper with toggle/selectAll/deselectAll/
 * isSelected/count helpers.
 *
 * Usage (inside a component):
 *   const sel = selection();
 *   <Checkbox checked={sel.isSelected(id)} onchange={() => sel.toggle(id)} />
 */
export function selection() {
  const selected = $state<Set<string>>(new Set());

  function toggle(id: string) {
    if (selected.has(id)) selected.delete(id);
    else selected.add(id);
  }

  function selectAll(ids: string[]) {
    selected.clear();
    for (const id of ids) selected.add(id);
  }

  function deselectAll() {
    selected.clear();
  }

  function isSelected(id: string) {
    return selected.has(id);
  }

  function isAllSelected(ids: string[]) {
    return ids.length > 0 && ids.every((id) => selected.has(id));
  }

  return {
    get selectedIds() {
      return selected;
    },
    get count() {
      return selected.size;
    },
    toggle,
    selectAll,
    deselectAll,
    isSelected,
    isAllSelected,
  };
}
