<script lang="ts">
  import type { Component } from "svelte";
  import { Button, TextInput } from "@prismedia/ui-svelte";
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

  function addPair() {
    const k = newKey.trim();
    const v = newValue.trim();
    if (!k || !v) return;
    if (validateKey) {
      const err = validateKey(k);
      if (err) { addError = err; return; }
    }
    if (validateValue) {
      const err = validateValue(v);
      if (err) { addError = err; return; }
    }
    addError = null;
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
  <div class="grid gap-2">
    {#if values.length > 0}
      <div class="grid min-w-0 grid-cols-[minmax(6rem,0.4fr)_minmax(0,1fr)_2rem] items-center gap-1">
        <span class="text-xs text-muted-foreground">{keyLabel}</span>
        <span class="text-xs text-muted-foreground">{valueLabel}</span>
        <span ></span>
      </div>
      <ul class="grid gap-1">
        {#each values as pair, i (i)}
          <li class="grid min-w-0 grid-cols-[minmax(6rem,0.4fr)_minmax(0,1fr)_2rem] items-center gap-1">
            <span class="truncate px-2 text-sm text-muted-foreground">{pair.key}</span>
            <TextInput
              type="text"
              inputmode={valueInputMode}
              value={pair.value}
              oninput={(e) => updateValue(i, (e.currentTarget as HTMLInputElement).value)}
              aria-label={label ?? pair.key}
              class="min-w-0"
            />
            <Button variant="ghost"
              type="button"
              size="icon"
              onclick={() => removePair(i)}
              aria-label={`Remove ${pair.key}`}
            >
              <X class="h-3 w-3" />
            </Button>
          </li>
        {/each}
      </ul>
    {/if}

    <div class="grid min-w-0 grid-cols-[minmax(6rem,0.4fr)_minmax(0,1fr)_2rem] items-center gap-1">
      <TextInput
        type="text"
        bind:value={newKey}
        onkeydown={handleAddKeydown}
        aria-label={keyLabel}
        placeholder={keyPlaceholder}
        class="min-w-0"
      />
      <TextInput
        type="text"
        inputmode={valueInputMode}
        bind:value={newValue}
        onkeydown={handleAddKeydown}
        aria-label={valueLabel}
        placeholder={valuePlaceholder}
        class="min-w-0"
      />
      <Button variant="ghost"
        type="button"
        size="icon"
        onclick={addPair}
        disabled={!newKey.trim() || !newValue.trim()}
        aria-label="Add entry"
      >
        <Plus class="h-3.5 w-3.5" />
      </Button>
    </div>
    {#if addError}
      <p class="text-[0.7rem] text-error-text">{addError}</p>
    {/if}
  </div>
</FormField>
