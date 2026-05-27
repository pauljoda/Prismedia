<script lang="ts">
  import { ChevronDown, Plus, SlidersHorizontal, Trash2 } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import {
    COLLECTION_RULE_FIELDS,
    type CollectionConditionValue,
    type CollectionEntityType,
    type CollectionOperator,
    type CollectionRuleCondition,
    type CollectionRuleFieldDef,
    type CollectionRuleGroup,
  } from "$lib/collections/models";

  interface Props {
    rule: CollectionRuleGroup;
    onChange: (rule: CollectionRuleGroup) => void;
    disabled?: boolean;
  }

  let { rule, onChange, disabled = false }: Props = $props();

  const entityKinds: { value: CollectionEntityType; label: string }[] = [
    { value: "video", label: "Video" },
    { value: "gallery", label: "Gallery" },
    { value: "image", label: "Image" },
    { value: "book", label: "Book" },
    { value: "audio-track", label: "Audio" },
  ];

  const conditions = $derived(
    rule.children.filter((child): child is CollectionRuleCondition => child.type === "condition"),
  );

  function updateGroup(patch: Partial<CollectionRuleGroup>) {
    onChange({ ...rule, ...patch });
  }

  function updateCondition(index: number, patch: Partial<CollectionRuleCondition>) {
    const next = [...conditions];
    next[index] = { ...next[index], ...patch };
    updateGroup({ children: next });
  }

  function addCondition() {
    const field = COLLECTION_RULE_FIELDS[0];
    updateGroup({
      children: [
        ...conditions,
        {
          type: "condition",
          entityTypes: [],
          field: field.field,
          operator: field.operators[0],
          value: "",
        },
      ],
    });
  }

  function removeCondition(index: number) {
    updateGroup({ children: conditions.filter((_, i) => i !== index) });
  }

  function fieldFor(condition: CollectionRuleCondition): CollectionRuleFieldDef {
    return COLLECTION_RULE_FIELDS.find((field) => field.field === condition.field) ?? COLLECTION_RULE_FIELDS[0];
  }

  function setField(index: number, fieldName: string) {
    const field = COLLECTION_RULE_FIELDS.find((item) => item.field === fieldName) ?? COLLECTION_RULE_FIELDS[0];
    updateCondition(index, {
      field: field.field,
      operator: field.operators[0],
      value: defaultValue(field, field.operators[0]),
    });
  }

  function setOperator(index: number, operator: string) {
    const condition = conditions[index];
    const field = fieldFor(condition);
    updateCondition(index, {
      operator: operator as CollectionOperator,
      value: defaultValue(field, operator as CollectionOperator),
    });
  }

  function defaultValue(field: CollectionRuleFieldDef, operator: CollectionOperator): CollectionConditionValue {
    if (operator === "is_null" || operator === "is_not_null" || operator === "is_true" || operator === "is_false") {
      return null;
    }
    if (operator === "between") return [0, 0];
    if (operator === "in" || operator === "not_in") return [];
    if (field.fieldType === "number") return 0;
    if (field.fieldType === "boolean") return true;
    return "";
  }

  function valueText(value: CollectionConditionValue): string {
    if (Array.isArray(value)) return value.join(", ");
    if (value === null || value === undefined) return "";
    return String(value);
  }

  function parseValue(field: CollectionRuleFieldDef, operator: CollectionOperator, raw: string): CollectionConditionValue {
    const trimmed = raw.trim();
    if (operator === "is_null" || operator === "is_not_null" || operator === "is_true" || operator === "is_false") {
      return null;
    }
    if (operator === "between") {
      const [min, max] = trimmed.split(",").map((part) => Number(part.trim()));
      return [Number.isFinite(min) ? min : 0, Number.isFinite(max) ? max : 0];
    }
    if (operator === "in" || operator === "not_in") {
      return trimmed
        .split(",")
        .map((part) => part.trim())
        .filter(Boolean);
    }
    if (field.fieldType === "number") {
      const value = Number(trimmed);
      return Number.isFinite(value) ? value : 0;
    }
    if (field.fieldType === "boolean") {
      return trimmed === "true";
    }
    return trimmed;
  }

  function toggleEntityType(index: number, kind: CollectionEntityType) {
    const current = conditions[index].entityTypes;
    updateCondition(index, {
      entityTypes: current.includes(kind)
        ? current.filter((item) => item !== kind)
        : [...current, kind],
    });
  }

  const selectClasses = cn(
    "min-w-0 w-full appearance-none border border-border-subtle bg-surface-2 px-2.5 py-2 pr-7 text-[0.78rem] text-text-primary",
    "shadow-[inset_0_2px_8px_rgba(0,0,0,0.30)] transition-colors outline-none",
    "focus:border-border-accent focus:shadow-[inset_0_2px_8px_rgba(0,0,0,0.30),0_0_0_1px_rgba(242,194,106,0.35),0_0_8px_rgba(242,194,106,0.15)]",
    "disabled:cursor-not-allowed disabled:opacity-50",
  );

  const inputClasses = cn(
    "min-w-0 w-full border border-border-subtle bg-surface-2 px-2.5 py-2 text-[0.78rem] text-text-primary",
    "shadow-[inset_0_2px_8px_rgba(0,0,0,0.30)] transition-colors outline-none placeholder:text-text-disabled",
    "focus:border-border-accent focus:shadow-[inset_0_2px_8px_rgba(0,0,0,0.30),0_0_0_1px_rgba(242,194,106,0.35),0_0_8px_rgba(242,194,106,0.15)]",
    "disabled:cursor-not-allowed disabled:opacity-50",
  );
</script>

<div class="surface-well p-4 space-y-3">
  <!-- Header -->
  <div class="flex items-center justify-between gap-4 flex-wrap">
    <div>
      <p class="text-kicker mb-1">Dynamic Match</p>
      <h3 class="m-0 font-heading text-[0.95rem] text-text-primary flex items-center gap-2">
        <SlidersHorizontal class="h-4 w-4 text-text-muted" />
        Filter Rules
      </h3>
    </div>
    <div class="relative inline-flex items-center gap-2">
      <span class="text-kicker">Match</span>
      <div class="relative">
        <select
          value={rule.operator}
          {disabled}
          onchange={(e) => updateGroup({ operator: (e.currentTarget as HTMLSelectElement).value as CollectionRuleGroup["operator"] })}
          class={selectClasses}
        >
          <option value="and">all rules</option>
          <option value="or">any rule</option>
          <option value="not">not these rules</option>
        </select>
        <ChevronDown class="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 h-3 w-3 text-text-muted" />
      </div>
    </div>
  </div>

  <!-- Rule list -->
  {#if conditions.length > 0}
    <div class="grid gap-2">
      {#each conditions as condition, i (i)}
        {@const field = fieldFor(condition)}
        {@const isNullary = ["is_null", "is_not_null", "is_true", "is_false"].includes(condition.operator)}
        <article class="border border-border-subtle bg-surface-1 p-3 space-y-2.5 transition-colors hover:border-border-default">
          <!-- Controls row -->
          <div class="grid gap-2 grid-cols-1 sm:grid-cols-[minmax(9rem,1fr)_minmax(8rem,0.85fr)_minmax(10rem,1fr)_auto]">
            <div class="relative">
              <select
                aria-label="Rule field"
                value={condition.field}
                {disabled}
                onchange={(e) => setField(i, (e.currentTarget as HTMLSelectElement).value)}
                class={selectClasses}
              >
                {#each COLLECTION_RULE_FIELDS as option (option.field)}
                  <option value={option.field}>{option.label}</option>
                {/each}
              </select>
              <ChevronDown class="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 h-3 w-3 text-text-muted" />
            </div>

            <div class="relative">
              <select
                aria-label="Rule operator"
                value={condition.operator}
                {disabled}
                onchange={(e) => setOperator(i, (e.currentTarget as HTMLSelectElement).value)}
                class={selectClasses}
              >
                {#each field.operators as operator (operator)}
                  <option value={operator}>{operator.replaceAll("_", " ")}</option>
                {/each}
              </select>
              <ChevronDown class="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 h-3 w-3 text-text-muted" />
            </div>

            {#if !isNullary}
              <input
                aria-label="Rule value"
                value={valueText(condition.value)}
                placeholder={field.enumValues ? field.enumValues.join(", ") : (condition.operator === "between" ? "min, max" : "Value")}
                {disabled}
                oninput={(e) =>
                  updateCondition(i, {
                    value: parseValue(field, condition.operator, (e.currentTarget as HTMLInputElement).value),
                  })}
                class={inputClasses}
              />
            {:else}
              <div class="hidden sm:block"></div>
            {/if}

            <button
              type="button"
              aria-label="Remove rule"
              title="Remove rule"
              {disabled}
              onclick={() => removeCondition(i)}
              class={cn(
                "inline-flex w-full sm:w-9 h-9 items-center justify-center border border-border-subtle bg-surface-2 text-text-muted transition-colors",
                "hover:border-error/50 hover:text-error-text hover:bg-error-muted/20",
                "disabled:cursor-not-allowed disabled:opacity-50",
              )}
            >
              <Trash2 class="h-3.5 w-3.5" />
            </button>
          </div>

          <!-- Entity type chips -->
          <div class="flex flex-wrap items-center gap-1.5">
            <span class="text-kicker mr-1">Types</span>
            {#each entityKinds as kind (kind.value)}
              {@const active = condition.entityTypes.length === 0 || condition.entityTypes.includes(kind.value)}
              <button
                type="button"
                {disabled}
                aria-pressed={active}
                onclick={() => toggleEntityType(i, kind.value)}
                class={cn(
                  "px-2 py-1 text-[0.68rem] font-medium border transition-all duration-fast",
                  "disabled:cursor-not-allowed disabled:opacity-50",
                  active
                    ? "border-border-accent bg-accent-950/40 text-text-accent shadow-[0_0_10px_rgba(242,194,106,0.10)]"
                    : "border-border-subtle bg-surface-2 text-text-disabled hover:text-text-muted hover:border-border-default",
                )}
              >
                {kind.label}
              </button>
            {/each}
          </div>
        </article>
      {/each}
    </div>
  {/if}

  <!-- Add rule -->
  <button
    type="button"
    {disabled}
    onclick={addCondition}
    class={cn(
      "inline-flex items-center gap-1.5 border border-dashed border-border-subtle bg-transparent px-3 py-2 text-[0.78rem] text-text-muted transition-all",
      "hover:border-border-accent hover:text-text-accent hover:bg-accent-950/10",
      "disabled:cursor-not-allowed disabled:opacity-50",
    )}
  >
    <Plus class="h-3.5 w-3.5" />
    Add Rule
  </button>
</div>
