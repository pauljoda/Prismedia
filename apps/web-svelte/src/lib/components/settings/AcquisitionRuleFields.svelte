<script lang="ts">
  import { untrack } from "svelte";
  import { Button, Checkbox, Select, TextInput } from "@prismedia/ui-svelte";
  import { CUSTOM_FORMAT_CONDITION_TYPE } from "$lib/api/generated/codes";
  import type { AcquisitionRulePresetView, CustomFormatSaveRequest } from "$lib/api/generated/model";
  import { literalTitleCondition } from "./acquisition-rule-editor";

  interface Props {
    form: CustomFormatSaveRequest;
    presets: AcquisitionRulePresetView[];
    editing?: boolean;
    onpreset?: (preset: AcquisitionRulePresetView) => void;
  }
  let { form = $bindable(), presets, editing = false, onpreset }: Props = $props();
  let advanced = $state(untrack(() => editing));
  let presetIndex = $state("");
  let literal = $state("");
  const selected = $derived(presetIndex === "" ? undefined : presets[Number(presetIndex)]);
  const conditionTypes = [
    { value: CUSTOM_FORMAT_CONDITION_TYPE.releaseTitle, label: "Title pattern (regex)" },
    { value: CUSTOM_FORMAT_CONDITION_TYPE.releaseGroup, label: "Release group (regex)" },
    { value: CUSTOM_FORMAT_CONDITION_TYPE.language, label: "Declared audio language" },
    { value: CUSTOM_FORMAT_CONDITION_TYPE.quality, label: "Quality code" },
  ];
  function applyPreset() {
    if (!selected) return;
    form.name = selected.name;
    form.conditions = selected.conditions.map((condition) => ({ ...condition }));
    onpreset?.(selected);
  }
  function applyLiteral() {
    if (!literal.trim()) return;
    form.name = literal.trim();
    form.conditions = [literalTitleCondition(literal)];
  }
</script>

<div class="space-y-3">
  <div class="flex items-center gap-1 border-b border-border-subtle pb-2" aria-label="Rule editor mode">
    <Button size="sm" variant={advanced ? "ghost" : "secondary"} aria-pressed={!advanced} onclick={() => (advanced = false)}>Basic</Button>
    <Button size="sm" variant={advanced ? "secondary" : "ghost"} aria-pressed={advanced} onclick={() => (advanced = true)}>Advanced</Button>
  </div>
  {#if !advanced}
    <div class="space-y-2">
      <label class="block space-y-1">
        <span class="text-label text-text-secondary">Start with a common preference</span>
        <Select size="sm" ariaLabel="Common preference" value={presetIndex} options={[{ value: "", label: "Choose a language or format…" }, ...presets.map((preset, index) => ({ value: String(index), label: preset.name }))]} onchange={(value) => (presetIndex = value)} />
      </label>
      {#if selected}<p class="text-xs leading-relaxed text-text-muted">{selected.description}</p>{/if}
      <Button size="sm" variant="secondary" disabled={!selected} onclick={applyPreset}>Use this preference</Button>
    </div>
    <div class="space-y-2 border-t border-border-subtle pt-3">
      <label class="block space-y-1"><span class="text-label text-text-secondary">Or match text in the release title</span>
        <TextInput size="sm" value={literal} oninput={(event) => (literal = event.currentTarget.value)} placeholder="For example, a release group or edition" /></label>
      <p class="text-xs text-text-muted">Spaces also match dots, underscores and dashes. Other characters are matched literally.</p>
      <Button size="sm" variant="secondary" disabled={!literal.trim()} onclick={applyLiteral}>Use this text</Button>
    </div>
    <p class="text-xs leading-relaxed text-text-muted">Current rule: {form.name || "Untitled"} · {form.conditions.length} condition(s). Open Advanced to edit its exact conditions. Changing views preserves your edits.</p>
  {:else}
    <p class="text-xs leading-relaxed text-text-muted">All required conditions must match, and at least one condition must match. Exclude inverts a condition. Patterns use .NET regular expressions, ignoring case.</p>
    {#each form.conditions as condition, index (index)}
      <div class="space-y-2 rounded-sm border border-border-subtle p-3">
        <div class="grid gap-2 sm:grid-cols-2">
          <Select size="sm" ariaLabel={`Condition ${index + 1} type`} value={condition.type} options={conditionTypes} onchange={(value) => (form.conditions[index].type = value as typeof condition.type)} />
          <TextInput size="sm" aria-label={`Condition ${index + 1} value`} value={condition.value} oninput={(event) => (form.conditions[index].value = event.currentTarget.value)} placeholder={condition.type === CUSTOM_FORMAT_CONDITION_TYPE.language ? "English or ENG" : "Pattern or quality code"} />
        </div>
        <div class="flex flex-wrap items-center gap-4">
          <label class="flex items-center gap-2 text-xs text-text-secondary"><Checkbox checked={condition.required} onchange={(event) => (form.conditions[index].required = event.currentTarget.checked)} />Required</label>
          <label class="flex items-center gap-2 text-xs text-text-secondary"><Checkbox checked={condition.negate} onchange={(event) => (form.conditions[index].negate = event.currentTarget.checked)} />Exclude</label>
          <Button size="sm" variant="ghost" disabled={form.conditions.length === 1} onclick={() => (form.conditions = form.conditions.filter((_, i) => i !== index))}>Remove condition</Button>
        </div>
      </div>
    {/each}
    <Button size="sm" variant="secondary" onclick={() => (form.conditions = [...form.conditions, literalTitleCondition("")])}>Add condition</Button>
  {/if}
</div>
