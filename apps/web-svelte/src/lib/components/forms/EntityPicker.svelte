<script lang="ts">
  import type { Component } from "svelte";
  import { ChoicePicker } from "@prismedia/ui-svelte";
  import FormField from "./FormField.svelte";

  export interface EntityPickerItem { id: string; title: string; thumbnailUrl: string | null; subtitle?: string; }
  interface Props {
    values: EntityPickerItem[];
    onChange: (values: EntityPickerItem[]) => void;
    onSearch: (query: string) => Promise<EntityPickerItem[]>;
    label?: string; icon?: Component; placeholder?: string; helper?: string; error?: string;
    disabled?: boolean; canAddNew?: boolean; addNewLabel?: string; mode?: "multi" | "single";
    maxResults?: number; showSelectedChips?: boolean;
  }
  let { values, onChange, onSearch, label, icon, placeholder = "Search…", helper, error,
    disabled = false, canAddNew = false, addNewLabel = "item", mode = "multi",
    maxResults = 20, showSelectedChips = true }: Props = $props();
  const id = $props.id();
  let query = $state("");
  let open = $state(false);
  let results = $state.raw<EntityPickerItem[]>([]);
  let searching = $state(false);
  let searchError = $state<string | null>(null);
  let retry = $state(0);
  const trimmed = $derived(query.trim());
  const selectedIds = $derived(new Set(values.map((item) => item.id)));
  const selected = $derived(values.map(choice));
  const available = $derived(results.filter((item) => !selectedIds.has(item.id)).slice(0, maxResults).map(choice));
  const canCreate = $derived(canAddNew && !!trimmed && !searching && !searchError
    && ![...results, ...values].some((item) => item.title.toLowerCase() === trimmed.toLowerCase()));

  function choice(item: EntityPickerItem) {
    return { value: item.id, label: item.title, image: item.thumbnailUrl, description: item.subtitle };
  }

  // Search belongs to the domain wrapper, with cancellation on query/visibility changes.
  $effect(() => {
    const term = trimmed;
    const search = onSearch;
    retry;
    if (!open || disabled) return;
    let active = true;
    searching = true;
    searchError = null;
    results = [];
    const timer = setTimeout(async () => {
      try {
        const found = await search(term);
        if (active) results = found;
      } catch (cause) {
        if (active) searchError = cause instanceof Error ? cause.message : "Search failed";
      } finally {
        if (active) searching = false;
      }
    }, term ? 200 : 0);
    return () => { active = false; clearTimeout(timer); };
  });

  function add(item: EntityPickerItem) {
    if (!selectedIds.has(item.id)) onChange(mode === "single" ? [item] : [...values, item]);
  }
</script>

<FormField {label} {icon} {helper} {error} htmlFor={id}>
  <ChoicePicker {id} label={label ?? addNewLabel} {placeholder} {disabled} invalid={!!error}
    describedBy={error || helper ? `${id}-message` : undefined}
    multiple={mode === "multi"} showSelected={showSelectedChips} bind:open bind:query
    options={available} {selected} loading={searching} error={searchError} onRetry={() => retry++}
    emptyText={trimmed ? `No ${addNewLabel}s found` : `Type to search ${addNewLabel}s`}
    createLabel={canCreate ? `Add "${trimmed}"` : undefined}
    onSelect={(value) => { const item = results.find((item) => item.id === value); if (item) add(item); }}
    onRemove={(value) => onChange(values.filter((item) => item.id !== value))}
    onCreate={() => add({ id: `new:${trimmed.toLowerCase()}`, title: trimmed, thumbnailUrl: null })} />
</FormField>
