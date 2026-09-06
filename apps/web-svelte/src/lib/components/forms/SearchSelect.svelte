<script lang="ts">
  import type { Component } from "svelte";
  import { ChoicePicker } from "@prismedia/ui-svelte";
  import FormField from "./FormField.svelte";
  export interface SearchOption { id?: string; name: string; count?: number; hint?: string; }
  interface Props {
    value: string; onChange: (value: string) => void; options: SearchOption[];
    label?: string; icon?: Component; placeholder?: string; helper?: string; error?: string;
    required?: boolean; disabled?: boolean; canAddNew?: boolean; allowClear?: boolean;
    maxResults?: number; emptyText?: string;
  }
  let { value, onChange, options, label, icon, placeholder = "Select…", helper, error,
    required = false, disabled = false, canAddNew = false, allowClear = true,
    maxResults = 50, emptyText = "No matches" }: Props = $props();
  const id = $props.id();
  let query = $state("");
  const trimmed = $derived(query.trim());
  const filtered = $derived(options.filter((item) => item.name.toLowerCase().includes(trimmed.toLowerCase()))
    .slice(0, maxResults).map((item) => ({ value: item.name, label: item.name, description: item.hint, count: item.count })));
  const canCreate = $derived(canAddNew && !!trimmed && !options.some((item) => item.name.toLowerCase() === trimmed.toLowerCase()));
</script>

<FormField {label} {icon} {helper} {error} {required} htmlFor={id}>
  <ChoicePicker {id} label={label ?? "options"} {placeholder} {disabled} invalid={!!error}
    describedBy={error || helper ? `${id}-message` : undefined}
    options={filtered} selected={value ? [{ value, label: value }] : []}
    bind:query {allowClear} {emptyText} createLabel={canCreate ? `Add "${trimmed}"` : undefined}
    onSelect={onChange} onRemove={() => onChange("")} onCreate={() => onChange(trimmed)} />
</FormField>
