<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import type { SettingDescriptor, SettingValue } from "$lib/api/prismedia";
  import {
    parseStringList,
    valueAsBoolean,
    valueAsNumber,
    valueAsString,
    valueAsStringListText,
  } from "$lib/settings/app-settings";
  import NumberStepper from "./NumberStepper.svelte";
  import ToggleCard from "./ToggleCard.svelte";

  interface Props {
    setting: SettingDescriptor;
    class?: string;
    disabled?: boolean;
    onCommit: (key: string, value: SettingValue) => void;
  }

  let { setting, class: className, disabled = false, onCommit }: Props = $props();

  let draftText = $state("");
  let draftNumber = $state(0);

  const inputId = $derived(`setting-${setting.key.replace(/[^a-zA-Z0-9_-]+/g, "-")}`);
  const numericMin = $derived(setting.constraints?.min ?? (setting.type === "decimal" ? 0 : 1));
  const numericMax = $derived(setting.constraints?.max ?? (setting.type === "decimal" ? 100 : 9999));
  const numericStep = $derived(setting.constraints?.step ?? (setting.type === "decimal" ? 0.05 : 1));

  $effect(() => {
    draftText = setting.type === "stringList"
      ? valueAsStringListText(setting.value)
      : valueAsString(setting.value);
    draftNumber = valueAsNumber(setting.value, numericMin);
  });

  function commitText() {
    const trimmed = draftText.trim();
    const next = setting.type === "stringList" ? parseStringList(trimmed) : trimmed;
    onCommit(setting.key, next);
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
</script>

{#if setting.type === "boolean"}
  <ToggleCard
    class={className}
    label={setting.label}
    description={setting.applyHint ? `${setting.description} ${setting.applyHint}` : setting.description}
    checked={valueAsBoolean(setting.value)}
    onChange={(checked) => {
      if (!disabled) onCommit(setting.key, checked);
    }}
  />
{:else if setting.type === "integer"}
  <NumberStepper
    class={className}
    label={setting.label}
    description={setting.applyHint ? `${setting.description} ${setting.applyHint}` : setting.description}
    value={valueAsNumber(setting.value, numericMin)}
    min={numericMin}
    max={numericMax}
    step={numericStep}
    onChange={(value) => {
      if (!disabled) commitNumber(value);
    }}
  />
{:else if setting.type === "decimal"}
  <div
    class={cn(
      "surface-card no-lift flex h-full min-h-[104px] flex-col justify-between p-3.5",
      disabled && "opacity-70",
      className,
    )}
  >
    <div class="mb-4 flex items-start justify-between gap-3">
      <div>
        <label class="control-label" for={inputId}>{setting.label}</label>
        <p class="mt-1 text-[0.68rem] text-text-muted">
          {setting.applyHint ? `${setting.description} ${setting.applyHint}` : setting.description}
        </p>
      </div>
      <span class="text-mono-sm border border-border-subtle bg-surface-1 px-2 py-0.5 text-text-accent shadow-well">
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
      onkeydown={(e) => {
        if (e.key === "Enter") commitNumber(draftNumber);
      }}
      class="setting-range"
      aria-label={setting.label}
    />
  </div>
{:else if setting.type === "select"}
  <div
    class={cn(
      "surface-card no-lift flex h-full min-h-[104px] flex-col gap-3 p-3.5",
      disabled && "opacity-70",
      className,
    )}
  >
    <div>
      <div class="control-label">{setting.label}</div>
      <p class="mt-1 text-[0.68rem] text-text-muted">
        {setting.applyHint ? `${setting.description} ${setting.applyHint}` : setting.description}
      </p>
    </div>

    <div class="grid gap-2 sm:grid-cols-2">
      {#each setting.options as option (option.value)}
        {@const active = valueAsString(setting.value) === option.value}
        <button
          type="button"
          disabled={disabled}
          onclick={() => onCommit(setting.key, option.value)}
          class={cn(
            "min-h-[58px] border p-2.5 text-left transition-all duration-fast",
            active
              ? "border-border-accent bg-surface-3 text-accent-400 shadow-[var(--shadow-glow-accent)]"
              : "border-border-default bg-surface-1 text-text-muted hover:border-border-subtle hover:bg-surface-2/60 hover:text-text-primary",
          )}
        >
          <span class="block text-[0.74rem] font-medium uppercase tracking-wider">
            {option.label}
          </span>
          {#if option.description}
            <span class="mt-1 block text-[0.64rem] leading-snug text-text-muted">
              {option.description}
            </span>
          {/if}
        </button>
      {/each}
    </div>
  </div>
{:else}
  <div
    class={cn(
      "surface-card no-lift flex h-full min-h-[104px] flex-col justify-between p-3.5",
      disabled && "opacity-70",
      className,
    )}
  >
    <div>
      <label class="control-label" for={inputId}>{setting.label}</label>
      <p class="mt-1 text-[0.68rem] text-text-muted">
        {setting.applyHint ? `${setting.description} ${setting.applyHint}` : setting.description}
      </p>
    </div>
    <input
      id={inputId}
      type="text"
      bind:value={draftText}
      disabled={disabled}
      onblur={commitText}
      onkeydown={(e) => {
        if (e.key === "Enter") (e.currentTarget as HTMLInputElement).blur();
      }}
      class={cn(
        "setting-text mt-3 w-full border border-border-default bg-surface-1 px-2.5 py-1.5 text-text-primary focus:border-border-accent focus:outline-none",
        setting.inputKind === "path" || setting.type === "stringList"
          ? "font-mono"
          : "",
      )}
      placeholder={valueAsStringListText(setting.defaultValue, valueAsString(setting.defaultValue))}
    />
  </div>
{/if}

<style>
  .setting-text {
    font-size: max(16px, 1rem);
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
    height: 0.38rem;
  }

  .setting-range::-webkit-slider-thumb {
    appearance: none;
    background: var(--color-surface-2, #141924);
    border: 1px solid var(--color-border-accent, #c49a5a);
    box-shadow: 0 0 10px rgb(0 0 0 / 0.8);
    height: 1rem;
    margin-top: -0.36rem;
    width: 0.62rem;
  }

  @media (min-width: 48rem) {
    input.setting-text {
      font-size: 0.74rem !important;
    }
  }
</style>
