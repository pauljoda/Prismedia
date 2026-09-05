<script lang="ts">
  import { Trash2 } from "@lucide/svelte";
  import { Button, ChoicePicker, Field, Select, TextInput } from "@prismedia/ui-svelte";
  import { COLLECTION_RULE_OPERATOR as OP } from "$lib/api/generated/codes";
  import { COLLECTION_ENTITY_TYPES, COLLECTION_RULE_FIELDS, type CollectionConditionValue,
    type CollectionRuleCondition, type CollectionOperator } from "$lib/collections/models";
  import { collectionFieldOptions, defaultConditionValue, isNullaryOperator } from "$lib/collections/rule-editor";
  import { displayNameForEntityKind } from "$lib/entities/entity-codes";
  import DateField from "$lib/components/forms/DateField.svelte";
  import FormField from "$lib/components/forms/FormField.svelte";
  import RuleEntityPicker from "./RuleEntityPicker.svelte";
  import { collectionRuleReferences } from "$lib/collections/rule-references";

  interface Props {
    condition: CollectionRuleCondition;
    onChange: (condition: CollectionRuleCondition) => void;
    onRemove: () => void;
    disabled?: boolean;
    libraryOptions?: { value: string; label: string }[];
  }
  let { condition, onChange, onRemove, disabled = false, libraryOptions = [] }: Props = $props();
  const id = $props.id();
  let typeQuery = $state("");
  const field = $derived(COLLECTION_RULE_FIELDS.find(field => field.field === condition.field) ?? COLLECTION_RULE_FIELDS[0]);
  const nullary = $derived(isNullaryOperator(condition.operator));
  const between = $derived(condition.operator === OP.between);
  const multiple = $derived(condition.operator === OP.in || condition.operator === OP.notIn);
  const scalarValue = $derived(Array.isArray(condition.value) || condition.value == null ? "" : String(condition.value));
  const range = $derived(Array.isArray(condition.value) ? condition.value.map(String) : ["", ""]);
  const kinds = COLLECTION_ENTITY_TYPES.map(value => ({ value, label: displayNameForEntityKind(value) }));
  const selectedKinds = $derived(kinds.filter(kind => condition.entityTypes.includes(kind.value)));
  const availableKinds = $derived(kinds.filter(kind =>
    (field.entityTypes.length === 0 || field.entityTypes.includes(kind.value)) &&
    !condition.entityTypes.includes(kind.value) &&
    kind.label.toLocaleLowerCase().includes(typeQuery.toLocaleLowerCase())));

  const operatorLabels: Record<CollectionOperator, string> = {
    [OP.equals]: "Is", [OP.notEquals]: "Is not", [OP.contains]: "Contains", [OP.notContains]: "Does not contain",
    [OP.greaterThan]: "Greater than", [OP.lessThan]: "Less than",
    [OP.greaterEqual]: "At least", [OP.lessEqual]: "At most", [OP.between]: "Between",
    [OP.in]: "Is one of", [OP.notIn]: "Is not one of", [OP.isNull]: "Is empty",
    [OP.isNotNull]: "Is not empty", [OP.isTrue]: "Yes", [OP.isFalse]: "No",
  };
  const operatorOptions = $derived(field.operators.map(value => ({
    value, label: field.fieldType === "date" && value === OP.greaterThan ? "After"
      : field.fieldType === "date" && value === OP.lessThan ? "Before" : operatorLabels[value],
  })));

  function changeValue(value: CollectionConditionValue) {
    onChange({ ...condition, value });
  }

  function changeField(value: string) {
    const next = COLLECTION_RULE_FIELDS.find(field => field.field === value);
    if (!next) return;
    typeQuery = "";
    onChange({ ...condition, field: next.field, entityTypes: next.entityTypes,
      operator: next.operators[0], value: defaultConditionValue(next, next.operators[0]) });
  }

  function changeOperator(value: string) {
    const next = field.operators.find(operator => operator === value);
    if (next) onChange({ ...condition, operator: next, value: defaultConditionValue(field, next) });
  }

  function changeRange(index: number, value: string) {
    const next: [string, string] = [range[0] ?? "", range[1] ?? ""];
    next[index] = value;
    changeValue(field.fieldType === "date" ? next : [Number(next[0]) || 0, Number(next[1]) || 0]);
  }

  function addType(value: string) {
    const kind = availableKinds.find(kind => kind.value === value);
    if (kind) onChange({ ...condition, entityTypes: [...condition.entityTypes, kind.value] });
  }
</script>

<article class="@container/rule min-w-0 rounded-lg border border-border-subtle p-4">
  <Field.Group>
    <Field.Group class="grid grid-cols-1 items-start @2xl/rule:grid-cols-3">
      <FormField label="Field" htmlFor={id + "-field"}>
        <Select id={id + "-field"} ariaLabel="Rule field" options={collectionFieldOptions}
          value={condition.field} {disabled} onchange={changeField} />
      </FormField>
      <FormField label="Comparison" htmlFor={id + "-operator"}>
        <Select id={id + "-operator"} ariaLabel="Rule operator" options={operatorOptions}
          value={condition.operator} {disabled} onchange={changeOperator} />
      </FormField>
      {#if between}
          <Field.Group role="group" aria-label="Value range" class="grid min-w-0 grid-cols-1 @lg/rule:grid-cols-2">
            {#if field.fieldType === "date"}
              <DateField label="From" value={range[0] ?? ""} {disabled} onChange={(value) => changeRange(0, value)} />
              <DateField label="To" value={range[1] ?? ""} {disabled} onChange={(value) => changeRange(1, value)} />
            {:else}
              <FormField label="Minimum" htmlFor={id + "-min"}>
                <TextInput id={id + "-min"} aria-label="Range minimum" type="number" value={range[0] ?? ""}
                  {disabled} oninput={(e) => changeRange(0, e.currentTarget.value)} />
              </FormField>
              <FormField label="Maximum" htmlFor={id + "-max"}>
                <TextInput id={id + "-max"} aria-label="Range maximum" type="number" value={range[1] ?? ""}
                  {disabled} oninput={(e) => changeRange(1, e.currentTarget.value)} />
              </FormField>
            {/if}
          </Field.Group>
      {:else if !nullary}
        {#if collectionRuleReferences[field.field]}
          {#key field.field}
            <RuleEntityPicker field={field.field} value={condition.value} {multiple} {disabled} onChange={changeValue} />
          {/key}
        {:else if field.fieldType === "date"}
          <DateField label="Date" value={scalarValue} {disabled} onChange={changeValue} />
        {:else}
          <FormField label={multiple ? "Values" : "Value"} htmlFor={id + "-value"}
            helper={multiple ? "Separate values with commas." : undefined}>
            {#if multiple}
              <TextInput id={id + "-value"} aria-label="Multi value (comma separated)"
                value={Array.isArray(condition.value) ? condition.value.join(", ") : ""}
                placeholder={field.enumValues?.join(", ")} {disabled}
                oninput={(e) => changeValue(e.currentTarget.value.split(",").map(value => value.trim()).filter(Boolean))} />
            {:else if field.fieldType === "number"}
              <TextInput id={id + "-value"} aria-label="Number value" type="number" value={scalarValue} {disabled}
                oninput={(e) => changeValue(Number(e.currentTarget.value) || 0)} />
            {:else if field.fieldType === "enum" && field.enumValues}
              <Select id={id + "-value"} ariaLabel="Enum value" options={field.enumValues.map(value => ({ value, label: value }))}
                value={scalarValue} {disabled} onchange={changeValue} />
            {:else if field.fieldType === "library"}
              <Select id={id + "-value"} ariaLabel="Library value" options={libraryOptions} value={scalarValue}
                placeholder={libraryOptions.length ? "Choose library" : "No visible libraries"}
                disabled={disabled || !libraryOptions.length} onchange={changeValue} />
            {:else}
              <TextInput id={id + "-value"} aria-label="Text value" value={scalarValue} {disabled}
                oninput={(e) => changeValue(e.currentTarget.value)} />
            {/if}
          </FormField>
        {/if}
      {/if}
    </Field.Group>
    <div class="flex min-w-0 flex-col gap-4 @lg/rule:flex-row @lg/rule:items-end">
      <FormField label="Entity types" htmlFor={id + "-types"} class="@lg/rule:max-w-md">
        <ChoicePicker id={id + "-types"} label="entity types" multiple selected={selectedKinds}
          options={availableKinds} bind:query={typeQuery} {disabled}
          placeholder={selectedKinds.length ? "Add a type…" : "All supported types"} onSelect={addType}
          onRemove={(value) => onChange({ ...condition, entityTypes: condition.entityTypes.filter(kind => kind !== value) })} />
      </FormField>
      <Button variant="ghost" class="self-end @lg/rule:ml-auto" aria-label="Remove condition" {disabled} onclick={onRemove}>
        <Trash2 data-icon="inline-start" />Remove
      </Button>
    </div>
  </Field.Group>
</article>
