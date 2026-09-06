<script lang="ts">
  import { ChoiceGroup, Field, InputGroup } from "@prismedia/ui-svelte";
  import FormField from "$lib/components/forms/FormField.svelte";
  import type { CollectionConditionValue, CollectionRuleFieldDef } from "$lib/collections/models";
  import { quantityFromInput, quantityUnitsForRule } from "$lib/collections/rule-quantities";

  interface Props {
    field: CollectionRuleFieldDef;
    value: CollectionConditionValue;
    between: boolean;
    disabled?: boolean;
    onChange: (value: CollectionConditionValue) => void;
  }
  let { field, value, between, disabled = false, onChange }: Props = $props();
  const id = $props.id();
  let selectedMultiplier = $state("1");
  const units = $derived(quantityUnitsForRule(field.field));
  const unit = $derived(units.find(unit => String(unit.multiplier) === selectedMultiplier) ?? units[0]);
  const options = $derived(units.map(unit => ({ value: String(unit.multiplier), label: unit.label })));
  const values = $derived(between ? (Array.isArray(value) ? value : [null, null]) : [value]);

  function displayValue(index: number): number | undefined {
    const raw = values[index];
    if (raw == null || raw === "" || Array.isArray(raw)) return undefined;
    const numeric = Number(raw);
    return Number.isFinite(numeric) ? numeric / unit.multiplier : undefined;
  }

  function changeValue(index: number, input: string) {
    const next = quantityFromInput(input, unit.multiplier);
    if (!between) return onChange(next);
    const bounds = [quantityFromInput(String(values[0] ?? ""), 1), quantityFromInput(String(values[1] ?? ""), 1)];
    bounds[index] = next;
    if (bounds[0] != null && bounds[1] != null) return onChange([bounds[0], bounds[1]]);
    onChange([bounds[0] == null ? "" : String(bounds[0]), bounds[1] == null ? "" : String(bounds[1])]);
  }
</script>

<Field.Group class="min-w-0 gap-3">
  <Field.Group class={between ? "grid min-w-0 grid-cols-2 gap-3" : "min-w-0"}>
    {#each (between ? ["Minimum", "Maximum"] : ["Value"]) as label, index (label)}
      <FormField {label} htmlFor={`${id}-${index}`}>
        <InputGroup.Root data-disabled={disabled}>
          <InputGroup.Input id={`${id}-${index}`} aria-label={`${field.label} ${label.toLowerCase()}`}
            type="number" step="any" inputmode="decimal" value={displayValue(index)} {disabled}
            oninput={(event) => changeValue(index, event.currentTarget.value)} />
          {#if unit.suffix}
            <InputGroup.Addon align="inline-end"><InputGroup.Text>{unit.suffix}</InputGroup.Text></InputGroup.Addon>
          {/if}
        </InputGroup.Root>
      </FormField>
    {/each}
  </Field.Group>
  {#if units.length > 1}
    <ChoiceGroup type="single" {options} value={String(unit.multiplier)} size="sm"
      ariaLabel={`${field.label} unit`} {disabled} onValueChange={(value) => selectedMultiplier = value} />
  {/if}
</Field.Group>
