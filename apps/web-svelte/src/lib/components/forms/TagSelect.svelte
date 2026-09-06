<script lang="ts">
  import type { Component } from "svelte";
  import { ChoicePicker } from "@prismedia/ui-svelte";
  import FormField from "./FormField.svelte";
  export interface TagOption { name: string; count?: number; isNew?: boolean; hint?: string; }
  interface Props {
    values: string[]; onChange: (values: string[]) => void; options: TagOption[];
    label?: string; icon?: Component; placeholder?: string; helper?: string; error?: string;
    disabled?: boolean; canAddNew?: boolean; maxResults?: number;
    chipVariant?: "neutral" | "accent"; newValues?: Set<string>;
  }
  let { values, onChange, options, label, icon, placeholder = "Add…", helper, error,
    disabled = false, canAddNew = true, maxResults = 12, chipVariant = "neutral", newValues }: Props = $props();
  const id = $props.id();
  let query = $state("");
  const trimmed = $derived(query.trim());
  const lower = $derived(trimmed.toLowerCase());
  const selectedNames = $derived(new Set(values.map((value) => value.toLowerCase())));
  const selected = $derived(values.map((value) => ({ value, label: value, isNew: newValues?.has(value.toLowerCase()) })));
  const filtered = $derived(options.filter((item) => !selectedNames.has(item.name.toLowerCase()) && item.name.toLowerCase().includes(lower))
    .slice(0, maxResults).map((item) => ({ value: item.name, label: item.name, description: item.hint, count: item.count })));
  const canCreate = $derived(canAddNew && !!trimmed && !selectedNames.has(lower) && !options.some((item) => item.name.toLowerCase() === lower));
  function add(value: string) {
    if (!selectedNames.has(value.toLowerCase())) onChange([...values, value]);
  }
</script>

<FormField {label} {icon} {helper} {error} htmlFor={id}>
  <ChoicePicker {id} label={label ?? "tags"} {placeholder} {disabled} invalid={!!error}
    describedBy={error || helper ? `${id}-message` : undefined}
    multiple bind:query options={filtered} {selected} accentChips={chipVariant === "accent"}
    createLabel={canCreate ? `Add "${trimmed}"` : undefined} onCreate={() => add(trimmed)}
    onSelect={add} onRemove={(value) => onChange(values.filter((item) => item !== value))} />
</FormField>
