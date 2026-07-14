<script lang="ts">
  import type { Component } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
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
  <div class="list-editor">
    {#if values.length > 0}
      <ul class="list-items">
        {#each values as value, i (i)}
          <li class="list-item">
            {#if editingIndex === i}
              <input
                type="text"
                bind:value={editingValue}
                onkeydown={handleEditKeydown}
                onblur={commitEdit}
                aria-label={label ? `${label} item` : "Item"}
                class={cn(
                  "flex-1 min-w-0 border border-border-accent bg-surface-2 px-2.5 py-1.5 text-sm text-text-primary",
                  "font-mono focus:outline-none focus:shadow-[var(--shadow-focus-accent)]",
                )}
              />
            {:else}
              <button
                type="button"
                class="item-value"
                onclick={() => startEdit(i)}
                title="Click to edit"
              >
                <GripVertical class="grip-icon h-3 w-3 shrink-0" />
                <span class="truncate">{value}</span>
              </button>
            {/if}
            <button
              type="button"
              class="item-remove"
              onclick={() => removeItem(i)}
              aria-label={`Remove ${value}`}
            >
              <X class="h-3 w-3" />
            </button>
          </li>
        {/each}
      </ul>
    {/if}

    <div class="list-add-row">
      <input
        type="text"
        bind:value={inputValue}
        onkeydown={handleInputKeydown}
        aria-label={label ?? "Add item"}
        {placeholder}
        class={cn(
          "flex-1 min-w-0 rounded-l-xs border bg-surface-2 px-2.5 py-1.5 text-sm text-text-primary shadow-[inset_0_2px_8px_rgba(0,0,0,0.30)]",
          "font-mono placeholder:text-text-disabled",
          "focus:border-border-accent focus:outline-none focus:shadow-[inset_0_2px_8px_rgba(0,0,0,0.30),0_0_0_1px_rgba(199, 201, 204,0.35),0_0_8px_rgba(199, 201, 204,0.15)]",
          inputError ? "border-error/60" : "border-border-subtle",
        )}
      />
      <button
        type="button"
        class="add-btn"
        onclick={addItem}
        disabled={!inputValue.trim()}
        aria-label="Add item"
      >
        <Plus class="h-3.5 w-3.5" />
      </button>
    </div>
    {#if inputError}
      <p class="text-[0.7rem] text-error-text">{inputError}</p>
    {/if}
  </div>
</FormField>

<style>
  .list-editor {
    display: grid;
    gap: 0.25rem;
  }

  .list-items {
    display: grid;
    gap: 1px;
    list-style: none;
    margin: 0;
    padding: 0;
  }

  .list-item {
    display: flex;
    align-items: center;
    gap: 0;
    min-width: 0;
  }

  .item-value {
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    gap: 0.4rem;
    padding: 0.4rem 0.65rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-right: none;
    border-radius: var(--radius-xs, 4px) 0 0 var(--radius-xs, 4px);
    background: var(--color-surface-2, #11151c);
    color: var(--color-text-primary, #e2e8f0);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.78rem;
    text-align: left;
    cursor: text;
    transition: border-color 0.15s, background 0.15s;
  }

  .item-value:hover {
    border-color: var(--color-border-accent, rgba(199, 155, 92, 0.24));
    background: color-mix(in srgb, var(--color-surface-2) 90%, var(--color-accent));
  }

  .grip-icon {
    color: var(--color-text-disabled, #4a5568);
  }

  .item-remove {
    display: grid;
    place-items: center;
    width: 2rem;
    align-self: stretch;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: 0 var(--radius-xs, 4px) var(--radius-xs, 4px) 0;
    background: var(--color-surface-2, #11151c);
    color: var(--color-text-disabled, #4a5568);
    cursor: pointer;
    transition: color 0.15s, background 0.15s, border-color 0.15s;
  }

  .item-remove:hover {
    color: var(--color-error-text, #fca5a5);
    background: color-mix(in srgb, var(--color-surface-2) 90%, var(--color-error));
    border-color: rgba(220, 80, 80, 0.3);
  }

  .list-add-row {
    display: flex;
    gap: 0;
    margin-top: 0.25rem;
  }

  .add-btn {
    display: grid;
    place-items: center;
    width: 2.25rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-left: none;
    border-radius: 0 var(--radius-xs, 4px) var(--radius-xs, 4px) 0;
    background: var(--color-surface-2, #11151c);
    color: var(--color-text-muted, #94a3b8);
    cursor: pointer;
    transition: color 0.15s, background 0.15s, border-color 0.15s;
  }

  .add-btn:hover:not(:disabled) {
    color: var(--color-accent, #c49a5a);
    border-color: var(--color-border-accent, rgba(199, 155, 92, 0.24));
    background: color-mix(in srgb, var(--color-surface-2) 92%, var(--color-accent));
  }

  .add-btn:disabled {
    opacity: 0.35;
    cursor: default;
  }
</style>
