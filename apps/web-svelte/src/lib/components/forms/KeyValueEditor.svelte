<script lang="ts">
  import type { Component } from "svelte";
  import { Button, Field, TextInput } from "@prismedia/ui-svelte";
  import { Plus, X } from "@lucide/svelte";
  import FormField from "./FormField.svelte";

  interface Props {
    values: { key: string; value: string }[];
    onChange: (values: { key: string; value: string }[]) => void;
    label?: string;
    icon?: Component;
    helper?: string;
    error?: string;
    keyPlaceholder?: string;
    valuePlaceholder?: string;
    keyLabel?: string;
    valueLabel?: string;
    valueInputMode?: "text" | "decimal";
    validateKey?: (key: string) => string | null;
    validateValue?: (value: string) => string | null;
  }

  let {
    values,
    onChange,
    label,
    icon,
    helper,
    error,
    keyPlaceholder = "key",
    valuePlaceholder = "value",
    keyLabel = "Key",
    valueLabel = "Value",
    valueInputMode = "text",
    validateKey,
    validateValue,
  }: Props = $props();

  let newKey = $state("");
  let newValue = $state("");
  let addError = $state<string | null>(null);
  let invalidField = $state<"key" | "value" | null>(null);
  const id = $props.id();

  function addPair() {
    const k = newKey.trim();
    const v = newValue.trim();
    if (!k || !v) return;
    if (validateKey) {
      const err = validateKey(k);
      if (err) { addError = err; invalidField = "key"; return; }
    }
    if (validateValue) {
      const err = validateValue(v);
      if (err) { addError = err; invalidField = "value"; return; }
    }
    addError = null;
    invalidField = null;
    onChange([...values, { key: k, value: v }]);
    newKey = "";
    newValue = "";
  }

  function removePair(index: number) {
    onChange(values.filter((_, i) => i !== index));
  }

  function updateValue(index: number, newVal: string) {
    const next = [...values];
    next[index] = { ...next[index], value: newVal };
    onChange(next);
  }

  function handleAddKeydown(e: KeyboardEvent) {
    if (e.key === "Enter") {
      e.preventDefault();
      addPair();
    }
  }

</script>

<FormField {label} {icon} {helper} {error}>
  <Field.Group class="@container gap-4">
    {#if values.length > 0}
      <ul class="grid min-w-0 gap-3">
        {#each values as pair, i (i)}
          <li class="grid min-w-0 grid-cols-[minmax(0,1fr)_auto] items-end gap-control-gap">
            <Field.Field class="min-w-0">
              <Field.Label for={`${id}-value-${i}`} class="break-all">{pair.key} {valueLabel}</Field.Label>
              <TextInput
                id={`${id}-value-${i}`}
                type="text"
                inputmode={valueInputMode}
                value={pair.value}
                oninput={(e) => updateValue(i, (e.currentTarget as HTMLInputElement).value)}
                class="min-w-0"
              />
            </Field.Field>
            <Button variant="ghost"
              type="button"
              size="icon"
              onclick={() => removePair(i)}
              aria-label={`Remove ${pair.key}`}
            >
              <X />
            </Button>
          </li>
        {/each}
      </ul>
    {/if}

    <div class="grid min-w-0 grid-cols-1 items-end gap-3 @min-[24rem]:grid-cols-2">
      <Field.Field data-invalid={invalidField === "key"} class="min-w-0">
        <Field.Label for={`${id}-new-key`}>New {keyLabel}</Field.Label>
        <TextInput
          id={`${id}-new-key`}
          type="text"
          bind:value={newKey}
          onkeydown={handleAddKeydown}
          aria-invalid={invalidField === "key"}
          aria-describedby={invalidField === "key" ? `${id}-error` : undefined}
          placeholder={keyPlaceholder}
          class="min-w-0"
        />
      </Field.Field>
      <Field.Field data-invalid={invalidField === "value"} class="min-w-0">
        <Field.Label for={`${id}-new-value`}>New {valueLabel}</Field.Label>
        <TextInput
          id={`${id}-new-value`}
          type="text"
          inputmode={valueInputMode}
          bind:value={newValue}
          onkeydown={handleAddKeydown}
          aria-invalid={invalidField === "value"}
          aria-describedby={invalidField === "value" ? `${id}-error` : undefined}
          placeholder={valuePlaceholder}
          class="min-w-0"
        />
      </Field.Field>
      <Button variant="secondary"
        type="button"
        class="@min-[24rem]:col-span-2 @min-[24rem]:justify-self-end"
        onclick={addPair}
        disabled={!newKey.trim() || !newValue.trim()}
        aria-label="Add entry"
      >
        <Plus data-icon="inline-start" />
        Add entry
      </Button>
    </div>
    {#if addError}
      <Field.Error id={`${id}-error`}>{addError}</Field.Error>
    {/if}
  </Field.Group>
</FormField>
