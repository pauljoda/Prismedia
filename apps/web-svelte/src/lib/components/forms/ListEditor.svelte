<script lang="ts">
  import { tick, type Component } from "svelte";
  import { Button, Field, TextInput } from "@prismedia/ui-svelte";
  import { Plus, X, Pencil } from "@lucide/svelte";
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
  let editingError = $state<string | null>(null);
  let editingInput = $state<HTMLInputElement | null>(null);
  const id = $props.id();

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

  async function startEdit(index: number) {
    editingIndex = index;
    editingValue = values[index];
    editingError = null;
    await tick();
    editingInput?.focus();
    editingInput?.select();
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
      if (err) { editingError = err; return; }
    }
    const next = [...values];
    next[editingIndex] = trimmed;
    onChange(next);
    editingIndex = null;
  }

  function cancelEdit() {
    editingIndex = null;
    editingError = null;
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
  <Field.Group class="gap-3">
    {#if values.length > 0}
      <ul class="grid min-w-0 gap-control-gap">
        {#each values as value, i (i)}
          <li class="flex min-w-0 flex-wrap items-center gap-control-gap">
            {#if editingIndex === i}
              <TextInput
                type="text"
                bind:value={editingValue}
                bind:ref={editingInput}
                onkeydown={handleEditKeydown}
                onblur={commitEdit}
                aria-label={label ? `${label} item` : "Item"}
                aria-invalid={Boolean(editingError)}
                aria-describedby={editingError ? `${id}-edit-error` : undefined}
                class="min-w-0 flex-1"
              />
            {:else}
              <Button variant="ghost"
                type="button"
                class="min-w-0 flex-1 justify-start"
                onclick={() => startEdit(i)}
                title="Edit item"
                aria-label={`Edit ${value}`}
              >
                <Pencil data-icon="inline-start" />
                <span class="truncate">{value}</span>
              </Button>
            {/if}
            <Button variant="ghost"
              type="button"
              size="icon"
              onclick={() => removeItem(i)}
              aria-label={`Remove ${value}`}
            >
              <X />
            </Button>
            {#if editingIndex === i && editingError}
              <Field.Error id={`${id}-edit-error`} class="w-full">{editingError}</Field.Error>
            {/if}
          </li>
        {/each}
      </ul>
    {/if}

    <Field.Field data-invalid={Boolean(inputError)}>
      <Field.Label for={`${id}-new-item`}>Add item</Field.Label>
      <div class="flex min-w-0 gap-control-gap">
        <TextInput
          id={`${id}-new-item`}
          type="text"
          bind:value={inputValue}
          onkeydown={handleInputKeydown}
          aria-invalid={Boolean(inputError)}
          aria-describedby={inputError ? `${id}-error` : undefined}
          {placeholder}
          class="min-w-0 flex-1"
        />
        <Button variant="secondary"
          type="button"
          onclick={addItem}
          disabled={!inputValue.trim()}
          aria-label="Add item"
        >
          <Plus data-icon="inline-start" />
          Add
        </Button>
      </div>
      {#if inputError}
        <Field.Error id={`${id}-error`}>{inputError}</Field.Error>
      {/if}
    </Field.Field>
  </Field.Group>
</FormField>
