<script lang="ts">
  import { Badge, Button, Field, Select, Slider, TextInput, Toggle, cn, type SelectOption } from "@prismedia/ui-svelte";
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

<Field.Field
  orientation={setting.type === "boolean" ? "horizontal" : setting.type === "integer" || setting.type === "select" ? "responsive" : "vertical"}
  data-disabled={disabled}
  class={cn("setting-row py-4", className)}
>
  <Field.Content>
    <Field.Label for={setting.type === "decimal" ? undefined : inputId} id={`${inputId}-label`}>
      {setting.label}
    </Field.Label>
    <Field.Description id={`${inputId}-description`}>{description}</Field.Description>
  </Field.Content>

  {#if setting.type === "boolean"}
    <Toggle id={inputId} ariaLabel={setting.label} ariaDescribedby={`${inputId}-description`}
      checked={valueAsBoolean(setting.value)} {disabled}
      onchange={(next) => { if (!disabled) onCommit(setting.key, next); }} />
  {:else if setting.type === "integer"}
    <div class="flex shrink-0 items-center gap-1">
      <Button variant="outline" size="icon" aria-label="Decrement"
        disabled={disabled || valueAsNumber(setting.value, numericMin) <= numericMin}
        onclick={() => commitNumber(valueAsNumber(setting.value, numericMin) - numericStep)}>−</Button>
      <TextInput id={inputId} inputmode="numeric" bind:value={draftIntText} {disabled}
        aria-describedby={`${inputId}-description`}
        onblur={commitIntText}
        onkeydown={(event) => { if (event.key === "Enter") event.currentTarget.blur(); }}
        class="w-20 text-center" />
      <Button variant="outline" size="icon" aria-label="Increment"
        disabled={disabled || valueAsNumber(setting.value, numericMin) >= numericMax}
        onclick={() => commitNumber(valueAsNumber(setting.value, numericMin) + numericStep)}>+</Button>
    </div>
  {:else if setting.type === "decimal"}
    <div class="flex items-center gap-4">
      <Slider type="single" min={numericMin} max={numericMax} step={numericStep}
        bind:value={draftNumber} {disabled} thumbLabel={setting.label}
        onValueCommit={commitNumber} />
      <Badge class="min-w-12 tabular-nums">{displayNumber(draftNumber)}</Badge>
    </div>
  {:else if setting.type === "select"}
    <div class="w-full shrink-0 sm:w-52">
      <Select id={inputId} options={selectOptions} value={valueAsString(setting.value)}
        ariaLabel={setting.label} ariaDescribedby={`${inputId}-description`} {disabled}
        onchange={(value) => { if (!disabled) onCommit(setting.key, value); }} />
    </div>
  {:else}
    <TextInput id={inputId} bind:value={draftText} {disabled}
      aria-describedby={`${inputId}-description`}
      onblur={commitText}
      onkeydown={(event) => { if (event.key === "Enter") event.currentTarget.blur(); }}
      placeholder={valueAsStringListText(setting.defaultValue, valueAsString(setting.defaultValue))} />
  {/if}
</Field.Field>
