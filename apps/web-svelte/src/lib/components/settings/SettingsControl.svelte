<script lang="ts">
  import { Select, Toggle, cn, type SelectOption } from "@prismedia/ui-svelte";
  import type { SettingDescriptor, SettingValue } from "$lib/api/settings";
  import {
    parseStringList,
    valueAsBoolean,
    valueAsNumber,
    valueAsString,
    valueAsStringListText,
  } from "$lib/settings/app-settings";

  interface Props {
    setting: SettingDescriptor;
    class?: string;
    disabled?: boolean;
    onCommit: (key: string, value: SettingValue) => void;
  }

  let { setting, class: className, disabled = false, onCommit }: Props = $props();

  let draftText = $state("");
  let draftNumber = $state(0);
  let draftIntText = $state("");

  const inputId = $derived(`setting-${setting.key.replace(/[^a-zA-Z0-9_-]+/g, "-")}`);
  const numericMin = $derived(setting.constraints?.min ?? (setting.type === "decimal" ? 0 : 1));
  const numericMax = $derived(setting.constraints?.max ?? (setting.type === "decimal" ? 100 : 9999));
  const numericStep = $derived(setting.constraints?.step ?? (setting.type === "decimal" ? 0.05 : 1));

  const selectOptions = $derived<SelectOption[]>(
    (setting.options ?? []).map((o) => ({ value: o.value, label: o.label })),
  );

  $effect(() => {
    draftText = setting.type === "stringList"
      ? valueAsStringListText(setting.value)
      : valueAsString(setting.value);
    draftNumber = valueAsNumber(setting.value, numericMin);
    draftIntText = String(valueAsNumber(setting.value, numericMin));
  });

  function commitText() {
    const trimmed = draftText.trim();
    const next = setting.type === "stringList" ? parseStringList(trimmed) : trimmed;
    onCommit(setting.key, next);
  }

  function commitIntText() {
    const parsed = parseInt(draftIntText, 10);
    if (Number.isNaN(parsed)) {
      draftIntText = String(valueAsNumber(setting.value, numericMin));
      return;
    }
    commitNumber(parsed);
  }

  function commitNumber(value: number) {
    const clamped = Math.max(numericMin, Math.min(numericMax, value));
    const rounded = setting.type === "integer" ? Math.round(clamped) : Number(clamped.toFixed(4));
    draftNumber = rounded;
    onCommit(setting.key, rounded);
  }

  function displayNumber(value: number): string {
    if (setting.type === "integer") return String(Math.round(value));
    return Number(value.toFixed(2)).toString();
  }

  const description = $derived(
    setting.applyHint ? `${setting.description} ${setting.applyHint}` : setting.description,
  );
</script>

<div class={cn("setting-row", disabled && "opacity-60", className)}>
  {#if setting.type === "boolean"}
    <button
      type="button"
      class="flex w-full items-center justify-between gap-4 py-3 text-left rounded-xs transition-colors hover:bg-surface-2/30"
      onclick={() => { if (!disabled) onCommit(setting.key, !valueAsBoolean(setting.value)); }}
    >
      <div class="min-w-0 flex-1">
        <div class="text-[0.82rem] font-medium text-text-primary">{setting.label}</div>
        <p class="mt-0.5 text-[0.68rem] leading-relaxed text-text-muted">{description}</p>
      </div>
      <Toggle checked={valueAsBoolean(setting.value)} {disabled} />
    </button>

  {:else if setting.type === "integer"}
    <div class="flex items-center justify-between gap-4 py-3">
      <div class="min-w-0 flex-1">
        <div class="text-[0.82rem] font-medium text-text-primary">{setting.label}</div>
        <p class="mt-0.5 text-[0.68rem] leading-relaxed text-text-muted">{description}</p>
      </div>
      <div class="flex items-center rounded-xs border border-border-default bg-surface-1 shadow-well shrink-0">
        <button
          type="button"
          onclick={() => { if (!disabled) commitNumber(valueAsNumber(setting.value, numericMin) - numericStep); }}
          class="px-2 py-1 text-text-muted hover:text-text-primary hover:bg-surface-2 transition-colors rounded-l-xs border-r border-border-subtle"
          aria-label="Decrement"
        >−</button>
        <input
          type="text"
          inputmode="numeric"
          bind:value={draftIntText}
          {disabled}
          onblur={commitIntText}
          onkeydown={(e) => { if (e.key === "Enter") (e.currentTarget as HTMLInputElement).blur(); }}
          class="w-12 bg-transparent text-center font-mono text-[0.78rem] text-text-primary py-1 outline-none"
          aria-label={setting.label}
        />
        <button
          type="button"
          onclick={() => { if (!disabled) commitNumber(valueAsNumber(setting.value, numericMin) + numericStep); }}
          class="px-2 py-1 text-text-muted hover:text-text-primary hover:bg-surface-2 transition-colors rounded-r-xs border-l border-border-subtle"
          aria-label="Increment"
        >+</button>
      </div>
    </div>

  {:else if setting.type === "decimal"}
    <div class="py-3">
      <div class="flex items-center justify-between gap-4">
        <div class="min-w-0 flex-1">
          <label class="text-[0.82rem] font-medium text-text-primary" for={inputId}>{setting.label}</label>
          <p class="mt-0.5 text-[0.68rem] leading-relaxed text-text-muted">{description}</p>
        </div>
        <span class="text-mono-sm shrink-0 rounded-xs border border-border-subtle bg-surface-1 px-2 py-0.5 text-text-accent shadow-well">
          {displayNumber(draftNumber)}
        </span>
      </div>
      <input
        id={inputId}
        type="range"
        min={numericMin}
        max={numericMax}
        step={numericStep}
        bind:value={draftNumber}
        disabled={disabled}
        onmouseup={() => commitNumber(draftNumber)}
        ontouchend={() => commitNumber(draftNumber)}
        onblur={() => commitNumber(draftNumber)}
        onkeydown={(e) => { if (e.key === "Enter") commitNumber(draftNumber); }}
        class="setting-range mt-2.5"
        aria-label={setting.label}
      />
    </div>

  {:else if setting.type === "select"}
    <div class="flex items-center justify-between gap-4 py-3">
      <div class="min-w-0 flex-1">
        <div class="text-[0.82rem] font-medium text-text-primary">{setting.label}</div>
        <p class="mt-0.5 text-[0.68rem] leading-relaxed text-text-muted">{description}</p>
      </div>
      <div class="w-44 shrink-0">
        <Select
          options={selectOptions}
          value={valueAsString(setting.value)}
          size="sm"
          onchange={(val) => { if (!disabled) onCommit(setting.key, val); }}
        />
      </div>
    </div>

  {:else}
    <div class="py-3">
      <div class="min-w-0">
        <label class="text-[0.82rem] font-medium text-text-primary" for={inputId}>{setting.label}</label>
        <p class="mt-0.5 text-[0.68rem] leading-relaxed text-text-muted">{description}</p>
      </div>
      <input
        id={inputId}
        type="text"
        bind:value={draftText}
        disabled={disabled}
        onblur={commitText}
        onkeydown={(e) => { if (e.key === "Enter") (e.currentTarget as HTMLInputElement).blur(); }}
        class={cn(
          "setting-text mt-2 w-full rounded-xs border border-border-default bg-surface-1 px-2.5 py-1.5 text-text-secondary shadow-[inset_0_1px_3px_rgba(0,0,0,0.25)]",
          "focus:border-border-accent focus:outline-none focus:shadow-[var(--shadow-focus-accent)] focus:text-text-primary",
          (setting.inputKind === "path" || setting.type === "stringList") && "font-mono",
        )}
        placeholder={valueAsStringListText(setting.defaultValue, valueAsString(setting.defaultValue))}
      />
    </div>
  {/if}
</div>

<style>
  .setting-text {
    font-size: max(16px, 0.75rem);
    line-height: 1.35;
  }

  .setting-range {
    appearance: none;
    background: transparent;
    cursor: pointer;
    height: 1rem;
    width: 100%;
  }

  .setting-range::-webkit-slider-runnable-track {
    background: var(--color-surface-4, #252b36);
    border: 1px solid var(--color-border-subtle, #354057);
    border-radius: var(--radius-full);
    height: 0.38rem;
  }

  .setting-range::-webkit-slider-thumb {
    appearance: none;
    background: var(--color-surface-2, #141924);
    border: 1px solid var(--color-border-accent, #c49a5a);
    border-radius: var(--radius-full);
    box-shadow: 0 0 8px rgba(199, 201, 204, 0.25);
    height: 1rem;
    margin-top: -0.36rem;
    width: 1rem;
  }

  .setting-range::-webkit-slider-thumb:hover {
    box-shadow: 0 0 12px rgba(199, 201, 204, 0.45);
  }

  @media (min-width: 48rem) {
    .setting-text {
      font-size: 0.75rem !important;
    }
  }
</style>
