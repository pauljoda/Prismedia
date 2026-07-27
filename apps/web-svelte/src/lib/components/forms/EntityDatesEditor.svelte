<script lang="ts">
  import { Calendar } from "@lucide/svelte";
  import type { KeyValuePair } from "$lib/components/forms";
  import {
    entityDateFieldsForKind,
    type EntityDateFieldDefinition,
  } from "$lib/entities/entity-date-fields";
  import DateField from "./DateField.svelte";
  import KeyValueEditor from "./KeyValueEditor.svelte";

  interface Props {
    entityKind: string;
    values: KeyValuePair[];
    onChange: (values: KeyValuePair[]) => void;
  }

  let { entityKind, values, onChange }: Props = $props();

  const fields = $derived(entityDateFieldsForKind(entityKind));
  const fieldCodes = $derived(new Set(fields.map((field) => field.code)));
  const otherDates = $derived(values.filter((value) => !fieldCodes.has(value.key as EntityDateFieldDefinition["code"])));

  function valueFor(code: string): string {
    return values.find((value) => value.key === code)?.value ?? "";
  }

  function exactDayValue(code: string): string {
    const value = valueFor(code);
    return /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : "";
  }

  function helperFor(field: EntityDateFieldDefinition): string {
    const value = valueFor(field.code);
    return value && !exactDayValue(field.code)
      ? `${field.helper} Current provider value: ${value}; choose an exact day to override it.`
      : field.helper;
  }

  function setDate(code: string, value: string) {
    const next = values.filter((item) => item.key !== code);
    if (value) next.push({ key: code, value });
    onChange(next);
  }

  function setOtherDates(nextOtherDates: KeyValuePair[]) {
    onChange([
      ...values.filter((value) => fieldCodes.has(value.key as EntityDateFieldDefinition["code"])),
      ...nextOtherDates,
    ]);
  }
</script>

<div class="grid gap-4">
  <div class="grid gap-3 sm:grid-cols-2">
    {#each fields as field (field.code)}
      <DateField
        value={exactDayValue(field.code)}
        onChange={(value) => setDate(field.code, value)}
        label={field.label}
        helper={helperFor(field)}
        icon={Calendar}
      />
    {/each}
  </div>

  {#if otherDates.length > 0}
    <KeyValueEditor
      values={otherDates}
      onChange={setOtherDates}
      label="Other dates"
      icon={Calendar}
      keyPlaceholder="date code"
      valuePlaceholder="YYYY, YYYY-MM, or YYYY-MM-DD"
      keyLabel="Code"
      valueLabel="Date"
    />
  {/if}
</div>
