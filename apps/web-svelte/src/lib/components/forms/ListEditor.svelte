<script lang="ts">
  import type { Component } from "svelte";
  import { Button, TextInput } from "@prismedia/ui-svelte";
  import { Plus, X, GripVertical } from "@lucide/svelte";
  import FormField from "./FormField.svelte";

  interface Props {
    values: string[];
    onChange: (values: string[]) => void;
    label?: string;
    icon?: Component;
    helper?: string;
    error?: string;
    placeholder?: string;
    validate?: (value: string) => string | null;
  }

  let {
    values,
    onChange,
    label,
    icon,
    helper,
    error,
    placeholder = "Add item…",
    validate,
  }: Props = $props();

  let inputValue = $state("");
  let inputError = $state<string | null>(null);
  let editingIndex = $state<number | null>(null);
  let editingValue = $state("");

  function addItem() {
    const trimmed = inputValue.trim();
    if (!trimmed) return;
    if (validate) {
      const err = validate(trimmed);
      if (err) {
        inputError = err;
        return;
      }
    }
    inputError = null;
    onChange([...values, trimmed]);
    inputValue = "";
  }

  function removeItem(index: number) {
    onChange(values.filter((_, i) => i !== index));
    if (editingIndex === index) {
      editingIndex = null;
    }
  }

  function startEdit(index: number) {
    editingIndex = index;
    editingValue = values[index];
  }

  function commitEdit() {
    if (editingIndex == null) return;
    const trimmed = editingValue.trim();
    if (!trimmed) {
      removeItem(editingIndex);
      return;
    }
    if (validate) {
      const err = validate(trimmed);
      if (err) return;
    }
    const next = [...values];
    next[editingIndex] = trimmed;
    onChange(next);
    editingIndex = null;
  }

  function cancelEdit() {
    editingIndex = null;
  }

  function handleInputKeydown(e: KeyboardEvent) {
    if (e.key === "Enter") {
      e.preventDefault();
      addItem();
    }
  }

  function handleEditKeydown(e: KeyboardEvent) {
    if (e.key === "Enter") {
      e.preventDefault();
      commitEdit();
    } else if (e.key === "Escape") {
      cancelEdit();
    }
  }
</script>

<FormField {label} {icon} {helper} {error}>
  <div class="grid gap-2">
    {#if values.length > 0}
      <ul class="grid gap-1">
        {#each values as value, i (i)}
          <li class="flex min-w-0 items-center gap-1">
            {#if editingIndex === i}
              <TextInput
                type="text"
                bind:value={editingValue}
                onkeydown={handleEditKeydown}
                onblur={commitEdit}
                aria-label={label ? `${label} item` : "Item"}
                class="min-w-0 flex-1"
              />
            {:else}
              <Button variant="ghost"
                type="button"
                class="min-w-0 flex-1 justify-start"
                onclick={() => startEdit(i)}
                title="Click to edit"
              >
                <GripVertical class="grip-icon h-3 w-3 shrink-0" />
                <span class="truncate">{value}</span>
              </Button>
            {/if}
            <Button variant="ghost"
              type="button"
              size="icon"
              onclick={() => removeItem(i)}
              aria-label={`Remove ${value}`}
            >
              <X class="h-3 w-3" />
            </Button>
          </li>
        {/each}
      </ul>
    {/if}

    <div class="mt-1 flex gap-1">
      <TextInput
        type="text"
        bind:value={inputValue}
        onkeydown={handleInputKeydown}
        aria-label={label ?? "Add item"}
        {placeholder}
        class="min-w-0 flex-1"
      />
      <Button variant="ghost"
        type="button"
        size="icon"
        onclick={addItem}
        disabled={!inputValue.trim()}
        aria-label="Add item"
      >
        <Plus class="h-3.5 w-3.5" />
      </Button>
    </div>
    {#if inputError}
      <p class="text-[0.7rem] text-error-text">{inputError}</p>
    {/if}
  </div>
</FormField>
