<script lang="ts">
  import { Trash2 } from "@lucide/svelte";
  import type {
    CollectionRuleCondition,
    CollectionEntityType,
    CollectionOperator,
    CollectionConditionValue,
    CollectionRuleFieldDef,
  } from "@prismedia/contracts";
  import { COLLECTION_RULE_FIELDS } from "@prismedia/contracts";
  import ChipInput from "../ChipInput.svelte";
  import type { SuggestionItem } from "$lib/collection-suggestions";

  interface Props {
    condition: CollectionRuleCondition;
    onChange: (condition: CollectionRuleCondition) => void;
    onDelete: () => void;
    availableTags?: SuggestionItem[];
    availablePerformers?: SuggestionItem[];
    availableStudios?: SuggestionItem[];
  }

  let {
    condition,
    onChange,
    onDelete,
    availableTags = [],
    availablePerformers = [],
    availableStudios = [],
  }: Props = $props();

  const entityTypeOptions: { value: CollectionEntityType; label: string }[] = [
    { value: "video", label: "Video" },
    { value: "gallery", label: "Gallery" },
    { value: "book", label: "Book" },
    { value: "image", label: "Image" },
    { value: "audio-track", label: "Audio" },
  ];

  const operatorLabels: Record<CollectionOperator, string> = {
    equals: "equals",
    not_equals: "not equals",
    contains: "contains",
    not_contains: "not contains",
    greater_than: ">",
    less_than: "<",
    greater_equal: ">=",
    less_equal: "<=",
    between: "between",
    in: "includes",
    not_in: "excludes",
    is_null: "is empty",
    is_not_null: "is not empty",
    is_true: "is true",
    is_false: "is false",
  };

  function getFieldDef(fieldName: string): CollectionRuleFieldDef | undefined {
    return COLLECTION_RULE_FIELDS.find((f) => f.field === fieldName);
  }

  function getAvailableFields(entityTypes: CollectionEntityType[]): CollectionRuleFieldDef[] {
    if (entityTypes.length === 0) return COLLECTION_RULE_FIELDS;
    return COLLECTION_RULE_FIELDS.filter(
      (f) => f.entityTypes.length === 0 || f.entityTypes.some((t) => entityTypes.includes(t)),
    );
  }

  function needsValueInput(operator: CollectionOperator): boolean {
    return !["is_null", "is_not_null", "is_true", "is_false"].includes(operator);
  }

  const fieldDef = $derived(getFieldDef(condition.field));
  const availableFields = $derived(getAvailableFields(condition.entityTypes));
  const operators = $derived(fieldDef?.operators ?? []);
  const showValue = $derived(needsValueInput(condition.operator));

  function toggleEntityType(type: CollectionEntityType) {
    const active = condition.entityTypes.includes(type);
    const types = active
      ? condition.entityTypes.filter((t) => t !== type)
      : [...condition.entityTypes, type];
    onChange({ ...condition, entityTypes: types });
  }

  function onFieldChange(fieldName: string) {
    const newFieldDef = getFieldDef(fieldName);
    const newOperators = newFieldDef?.operators ?? [];
    const newOperator = newOperators.includes(condition.operator)
      ? condition.operator
      : newOperators[0] ?? "equals";
    onChange({ ...condition, field: fieldName, operator: newOperator, value: null });
  }

  function onOperatorChange(op: CollectionOperator) {
    onChange({ ...condition, operator: op });
  }

  function onValueChange(value: CollectionConditionValue) {
    onChange({ ...condition, value });
  }

  const relationSuggestions = $derived.by(() => {
    if (!fieldDef || fieldDef.fieldType !== "relation") return [] as SuggestionItem[];
    switch (fieldDef.field) {
      case "tags":
        return availableTags;
      case "performers":
        return availablePerformers;
      case "studio":
        return availableStudios;
      default:
        return [];
    }
  });
</script>

<div class="flex items-start gap-1.5 flex-wrap">
  <div class="flex items-center gap-0.5 pt-1">
    {#each entityTypeOptions as option (option.value)}
      {@const active = condition.entityTypes.includes(option.value)}
      <button
        type="button"
        onclick={() => toggleEntityType(option.value)}
        class={`px-1.5 py-0.5 text-[0.6rem] font-mono uppercase transition-colors ${
          active
            ? "bg-accent-brass/20 text-text-accent border border-accent-brass/30"
            : "bg-surface-2 text-text-disabled border border-border-subtle hover:text-text-muted"
        }`}
      >
        {option.label}
      </button>
    {/each}
  </div>

  <select
    value={condition.field}
    onchange={(e) => onFieldChange((e.currentTarget as HTMLSelectElement).value)}
    class="px-2 py-1 text-[0.75rem] bg-surface-1 border border-border-default text-text-primary focus:outline-none focus:border-accent-brass/30"
  >
    {#each availableFields as f (f.field)}
      <option value={f.field}>{f.label}</option>
    {/each}
  </select>

  <select
    value={condition.operator}
    onchange={(e) => onOperatorChange((e.currentTarget as HTMLSelectElement).value as CollectionOperator)}
    class="px-2 py-1 text-[0.75rem] bg-surface-1 border border-border-default text-text-primary focus:outline-none focus:border-accent-brass/30"
  >
    {#each operators as op (op)}
      <option value={op}>{operatorLabels[op]}</option>
    {/each}
  </select>

  {#if showValue && fieldDef}
    <div class="flex-1 min-w-[180px]">
      {#if fieldDef.fieldType === "enum" && fieldDef.enumValues}
        {@const selected = Array.isArray(condition.value) ? (condition.value as string[]) : []}
        <div class="flex flex-wrap gap-0.5">
          {#each fieldDef.enumValues as v (v)}
            {@const isSelected = selected.includes(v)}
            <button
              type="button"
              onclick={() => {
                const next = isSelected ? selected.filter((s) => s !== v) : [...selected, v];
                onValueChange(next);
              }}
              class={`px-1.5 py-0.5 text-[0.65rem] font-mono transition-colors ${
                isSelected
                  ? "bg-accent-brass/20 text-text-accent border border-accent-brass/30"
                  : "bg-surface-2 text-text-muted border border-border-subtle hover:text-text-secondary"
              }`}
            >
              {v}
            </button>
          {/each}
        </div>
      {:else if fieldDef.fieldType === "relation"}
        {#if fieldDef.field === "videoSeriesId"}
          <input
            type="text"
            value={typeof condition.value === "string" ? condition.value : ""}
            oninput={(e) => onValueChange((e.currentTarget as HTMLInputElement).value)}
            placeholder="folder ID..."
            class="w-full px-2 py-1 text-[0.75rem] bg-surface-1 border border-border-default text-text-primary placeholder:text-text-disabled focus:outline-none focus:border-accent-brass/30"
          />
        {:else}
          {@const currentValues = Array.isArray(condition.value) ? (condition.value as string[]) : []}
          <ChipInput
            values={currentValues}
            onChange={(vals) => onValueChange(vals)}
            suggestions={relationSuggestions}
            placeholder={`Search ${fieldDef.label.toLowerCase()}...`}
          />
        {/if}
      {:else if condition.operator === "between"}
        {@const range = Array.isArray(condition.value) ? (condition.value as [number, number]) : [0, 0]}
        <div class="flex items-center gap-1">
          <input
            type="number"
            value={range[0]}
            oninput={(e) =>
              onValueChange([Number((e.currentTarget as HTMLInputElement).value), range[1]])}
            class="px-2 py-1 text-[0.75rem] bg-surface-1 border border-border-default text-text-primary w-20 focus:outline-none focus:border-accent-brass/30"
          />
          <span class="text-[0.7rem] text-text-disabled">to</span>
          <input
            type="number"
            value={range[1]}
            oninput={(e) =>
              onValueChange([range[0], Number((e.currentTarget as HTMLInputElement).value)])}
            class="px-2 py-1 text-[0.75rem] bg-surface-1 border border-border-default text-text-primary w-20 focus:outline-none focus:border-accent-brass/30"
          />
        </div>
      {:else if fieldDef.fieldType === "number"}
        <input
          type="number"
          value={typeof condition.value === "number" ? condition.value : ""}
          oninput={(e) => onValueChange(Number((e.currentTarget as HTMLInputElement).value))}
          class="w-full px-2 py-1 text-[0.75rem] bg-surface-1 border border-border-default text-text-primary focus:outline-none focus:border-accent-brass/30"
        />
      {:else if fieldDef.fieldType === "date"}
        <input
          type="date"
          value={typeof condition.value === "string" ? condition.value : ""}
          oninput={(e) => onValueChange((e.currentTarget as HTMLInputElement).value)}
          class="w-full px-2 py-1 text-[0.75rem] bg-surface-1 border border-border-default text-text-primary focus:outline-none focus:border-accent-brass/30"
        />
      {:else}
        <input
          type="text"
          value={typeof condition.value === "string" ? condition.value : ""}
          oninput={(e) => onValueChange((e.currentTarget as HTMLInputElement).value)}
          placeholder="value..."
          class="w-full px-2 py-1 text-[0.75rem] bg-surface-1 border border-border-default text-text-primary placeholder:text-text-disabled focus:outline-none focus:border-accent-brass/30"
        />
      {/if}
    </div>
  {/if}

  <button
    type="button"
    onclick={onDelete}
    class="p-1 pt-1.5 text-text-disabled hover:text-error-text transition-colors"
    aria-label="Delete condition"
  >
    <Trash2 class="h-3.5 w-3.5" />
  </button>
</div>
