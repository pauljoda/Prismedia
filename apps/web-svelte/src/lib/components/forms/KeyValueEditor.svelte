<script lang="ts">
  import type { Component } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
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

  const inputBase = cn(
    "min-w-0 border border-border-subtle bg-surface-2 px-2.5 py-1.5 text-sm text-text-primary shadow-[inset_0_2px_8px_rgba(0,0,0,0.30)]",
    "font-mono placeholder:text-text-disabled",
    "focus:border-border-accent focus:outline-none focus:shadow-[inset_0_2px_8px_rgba(0,0,0,0.30),0_0_0_1px_rgba(199, 201, 204,0.35),0_0_8px_rgba(199, 201, 204,0.15)]",
  );
</script>

<FormField {label} {icon} {helper} {error}>
  <div class="kv-editor">
    {#if values.length > 0}
      <div class="kv-header">
        <span class="kv-col-label">{keyLabel}</span>
        <span class="kv-col-label">{valueLabel}</span>
        <span class="kv-col-spacer"></span>
      </div>
      <ul class="kv-items">
        {#each values as pair, i (i)}
          <li class="kv-item">
            <span class="kv-key">{pair.key}</span>
            <input
              type="text"
              inputmode={valueInputMode}
              value={pair.value}
              oninput={(e) => updateValue(i, (e.currentTarget as HTMLInputElement).value)}
              aria-label={label ?? pair.key}
              class={cn(inputBase, "flex-1")}
            />
            <button
              type="button"
              class="kv-remove"
              onclick={() => removePair(i)}
              aria-label={`Remove ${pair.key}`}
            >
              <X class="h-3 w-3" />
            </button>
          </li>
        {/each}
      </ul>
    {/if}

    <div class="kv-add-row">
      <input
        type="text"
        bind:value={newKey}
        onkeydown={handleAddKeydown}
        aria-label={keyLabel}
        placeholder={keyPlaceholder}
        class={cn(inputBase, "kv-add-key")}
      />
      <input
        type="text"
        inputmode={valueInputMode}
        bind:value={newValue}
        onkeydown={handleAddKeydown}
        aria-label={valueLabel}
        placeholder={valuePlaceholder}
        class={cn(inputBase, "flex-1")}
      />
      <button
        type="button"
        class="kv-add-btn"
        onclick={addPair}
        disabled={!newKey.trim() || !newValue.trim()}
        aria-label="Add entry"
      >
        <Plus class="h-3.5 w-3.5" />
      </button>
    </div>
    {#if addError}
      <p class="text-[0.7rem] text-error-text">{addError}</p>
    {/if}
  </div>
</FormField>

<style>
  .kv-editor {
    display: grid;
    gap: 0.25rem;
  }

  .kv-header {
    display: grid;
    grid-template-columns: minmax(6rem, 0.4fr) 1fr 2rem;
    gap: 0;
    padding: 0 0 0.15rem;
  }

  .kv-col-label {
    color: var(--color-text-disabled, #4a5568);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    padding-left: 0.65rem;
  }

  .kv-col-spacer {
    width: 2rem;
  }

  .kv-items {
    display: grid;
    gap: 1px;
    list-style: none;
    margin: 0;
    padding: 0;
  }

  .kv-item {
    display: grid;
    grid-template-columns: minmax(6rem, 0.4fr) 1fr 2rem;
    gap: 0;
    align-items: center;
    min-width: 0;
  }

  .kv-key {
    display: flex;
    align-items: center;
    padding: 0.4rem 0.65rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-right: none;
    border-radius: var(--radius-xs, 4px) 0 0 var(--radius-xs, 4px);
    background: color-mix(in srgb, var(--color-surface-2) 80%, var(--color-surface-3));
    color: var(--color-text-muted, #94a3b8);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.78rem;
    font-weight: 600;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    align-self: stretch;
  }

  .kv-remove {
    display: grid;
    place-items: center;
    width: 2rem;
    align-self: stretch;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-left: none;
    border-radius: 0 var(--radius-xs, 4px) var(--radius-xs, 4px) 0;
    background: var(--color-surface-2, #11151c);
    color: var(--color-text-disabled, #4a5568);
    cursor: pointer;
    transition: color 0.15s, background 0.15s;
  }

  .kv-remove:hover {
    color: var(--color-error-text, #fca5a5);
    background: color-mix(in srgb, var(--color-surface-2) 90%, var(--color-error));
  }

  .kv-add-row {
    display: grid;
    grid-template-columns: minmax(6rem, 0.4fr) 1fr 2.25rem;
    gap: 0;
    margin-top: 0.25rem;
  }

  :global(.kv-add-key) {
    border-right: none !important;
    border-radius: var(--radius-xs, 4px) 0 0 var(--radius-xs, 4px) !important;
  }

  .kv-add-btn {
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

  .kv-add-btn:hover:not(:disabled) {
    color: var(--color-accent, #c7c9cc);
    border-color: var(--color-border-accent, rgba(199, 155, 92, 0.24));
    background: color-mix(in srgb, var(--color-surface-2) 92%, var(--color-accent));
  }

  .kv-add-btn:disabled {
    opacity: 0.35;
    cursor: default;
  }
</style>
