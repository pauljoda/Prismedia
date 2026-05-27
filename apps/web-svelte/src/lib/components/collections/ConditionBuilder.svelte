<script lang="ts">
  import { Plus, SlidersHorizontal, Trash2 } from "@lucide/svelte";
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
</script>

<div class="rule-builder">
  <div class="rule-builder-head">
    <div>
      <p class="rule-kicker">Dynamic Match</p>
      <h3><SlidersHorizontal class="h-4 w-4" /> Filter Rules</h3>
    </div>
    <label class="operator-select">
      <span>Match</span>
      <select
        value={rule.operator}
        {disabled}
        onchange={(e) => updateGroup({ operator: (e.currentTarget as HTMLSelectElement).value as CollectionRuleGroup["operator"] })}
      >
        <option value="and">all rules</option>
        <option value="or">any rule</option>
        <option value="not">not these rules</option>
      </select>
    </label>
  </div>

  <div class="rule-list">
    {#each conditions as condition, i (i)}
      {@const field = fieldFor(condition)}
      <article class="rule-row">
        <div class="rule-controls">
          <select
            aria-label="Rule field"
            value={condition.field}
            {disabled}
            onchange={(e) => setField(i, (e.currentTarget as HTMLSelectElement).value)}
          >
            {#each COLLECTION_RULE_FIELDS as option (option.field)}
              <option value={option.field}>{option.label}</option>
            {/each}
          </select>

          <select
            aria-label="Rule operator"
            value={condition.operator}
            {disabled}
            onchange={(e) => setOperator(i, (e.currentTarget as HTMLSelectElement).value)}
          >
            {#each field.operators as operator (operator)}
              <option value={operator}>{operator.replaceAll("_", " ")}</option>
            {/each}
          </select>

          {#if !["is_null", "is_not_null", "is_true", "is_false"].includes(condition.operator)}
            {#if field.enumValues}
              <input
                aria-label="Rule value"
                value={valueText(condition.value)}
                placeholder={field.enumValues.join(", ")}
                {disabled}
                oninput={(e) =>
                  updateCondition(i, {
                    value: parseValue(field, condition.operator, (e.currentTarget as HTMLInputElement).value),
                  })}
              />
            {:else}
              <input
                aria-label="Rule value"
                value={valueText(condition.value)}
                placeholder={condition.operator === "between" ? "min, max" : "Value"}
                {disabled}
                oninput={(e) =>
                  updateCondition(i, {
                    value: parseValue(field, condition.operator, (e.currentTarget as HTMLInputElement).value),
                  })}
              />
            {/if}
          {/if}

          <button
            type="button"
            class="icon-btn danger"
            aria-label="Remove rule"
            title="Remove rule"
            {disabled}
            onclick={() => removeCondition(i)}
          >
            <Trash2 class="h-3.5 w-3.5" />
          </button>
        </div>

        <div class="kind-chips" aria-label="Entity types">
          <span>Types</span>
          {#each entityKinds as kind (kind.value)}
            <button
              type="button"
              class={cn(
                "kind-chip",
                condition.entityTypes.length === 0 || condition.entityTypes.includes(kind.value) ? "active" : "",
              )}
              {disabled}
              aria-pressed={condition.entityTypes.length === 0 || condition.entityTypes.includes(kind.value)}
              onclick={() => toggleEntityType(i, kind.value)}
            >
              {kind.label}
            </button>
          {/each}
        </div>
      </article>
    {/each}
  </div>

  <button type="button" class="add-rule" {disabled} onclick={addCondition}>
    <Plus class="h-4 w-4" />
    Add Rule
  </button>
</div>

<style>
  .rule-builder {
    display: grid;
    gap: 0.85rem;
    border: 1px solid var(--color-border-subtle);
    background: linear-gradient(180deg, rgba(21, 26, 40, 0.86), rgba(12, 15, 21, 0.92));
    padding: 1rem;
  }

  .rule-builder-head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
  }

  .rule-kicker {
    margin: 0 0 0.25rem;
    font-family: var(--font-mono);
    font-size: 0.62rem;
    letter-spacing: 0;
    text-transform: uppercase;
    color: var(--color-text-disabled);
  }

  h3 {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin: 0;
    font-family: var(--font-heading);
    font-size: 0.98rem;
    color: var(--color-text-primary);
  }

  .operator-select {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    font-family: var(--font-mono);
    font-size: 0.68rem;
    color: var(--color-text-muted);
  }

  select,
  input {
    min-width: 0;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
    color: var(--color-text-primary);
    font-size: 0.8rem;
    padding: 0.48rem 0.6rem;
    outline: none;
  }

  select:focus,
  input:focus {
    border-color: var(--color-border-accent);
    box-shadow: var(--shadow-focus-accent);
  }

  .rule-list {
    display: grid;
    gap: 0.65rem;
  }

  .rule-row {
    display: grid;
    gap: 0.6rem;
    border: 1px solid rgba(255, 255, 255, 0.06);
    background: rgba(7, 10, 15, 0.46);
    padding: 0.75rem;
  }

  .rule-controls {
    display: grid;
    grid-template-columns: minmax(9rem, 1fr) minmax(8rem, 0.85fr) minmax(10rem, 1fr) auto;
    gap: 0.5rem;
  }

  .icon-btn {
    display: inline-flex;
    width: 2.2rem;
    height: 2.2rem;
    align-items: center;
    justify-content: center;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
    color: var(--color-text-muted);
  }

  .icon-btn:hover:not(:disabled) {
    border-color: var(--color-error);
    color: var(--color-error-text);
  }

  .kind-chips {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.35rem;
    color: var(--color-text-disabled);
    font-family: var(--font-mono);
    font-size: 0.66rem;
  }

  .kind-chip,
  .add-rule {
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
    color: var(--color-text-muted);
    transition: border-color 0.16s, color 0.16s, box-shadow 0.16s;
  }

  .kind-chip {
    padding: 0.3rem 0.5rem;
    font-size: 0.68rem;
  }

  .kind-chip.active {
    border-color: rgba(242, 194, 106, 0.48);
    color: var(--color-text-accent);
    box-shadow: 0 0 12px rgb(242 194 106 / 0.12);
  }

  .add-rule {
    display: inline-flex;
    width: fit-content;
    align-items: center;
    gap: 0.45rem;
    padding: 0.55rem 0.8rem;
    font-size: 0.78rem;
  }

  .add-rule:hover:not(:disabled) {
    border-color: rgba(242, 194, 106, 0.5);
    color: var(--color-text-accent);
  }

  button:disabled,
  select:disabled,
  input:disabled {
    cursor: not-allowed;
    opacity: 0.55;
  }

  @media (max-width: 820px) {
    .rule-builder-head {
      align-items: stretch;
      flex-direction: column;
    }

    .rule-controls {
      grid-template-columns: 1fr;
    }

    .icon-btn {
      width: 100%;
    }
  }
</style>
