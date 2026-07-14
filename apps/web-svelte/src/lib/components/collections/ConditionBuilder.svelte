<script lang="ts">
  import {
    BookOpen,
    Calendar,
    ChevronDown,
    Clapperboard,
    Disc3,
    Film,
    FolderOpen,
    Hash,
    Image as ImageIcon,
    Images,
    Link2,
    ListChecks,
    MicVocal,
    Music,
    Plus,
    ToggleRight,
    Type as TypeIcon,
    X,
  } from "@lucide/svelte";
  import type { Component } from "svelte";
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
  import { ENTITY_KIND } from "$lib/entities/entity-codes";

  interface CollectionRuleSelectOption {
    value: string;
    label: string;
  }

  interface Props {
    rule: CollectionRuleGroup;
    onChange: (rule: CollectionRuleGroup) => void;
    disabled?: boolean;
    libraryOptions?: CollectionRuleSelectOption[];
  }

  let { rule, onChange, disabled = false, libraryOptions = [] }: Props = $props();

  const entityKinds: { value: CollectionEntityType; label: string; icon: Component }[] = [
    { value: ENTITY_KIND.video, label: "Video", icon: Film },
    { value: ENTITY_KIND.movie, label: "Movie", icon: Clapperboard },
    { value: ENTITY_KIND.videoSeries, label: "Series", icon: FolderOpen },
    { value: ENTITY_KIND.gallery, label: "Gallery", icon: Images },
    { value: ENTITY_KIND.image, label: "Image", icon: ImageIcon },
    { value: ENTITY_KIND.book, label: "Book", icon: BookOpen },
    { value: ENTITY_KIND.musicArtist, label: "Artist", icon: MicVocal },
    { value: ENTITY_KIND.audioLibrary, label: "Album", icon: Disc3 },
    { value: ENTITY_KIND.audioTrack, label: "Track", icon: Music },
  ];

  const fieldTypeMeta: Record<
    CollectionRuleFieldDef["fieldType"],
    { icon: Component; label: string }
  > = {
    text: { icon: TypeIcon, label: "Text" },
    number: { icon: Hash, label: "Number" },
    date: { icon: Calendar, label: "Date" },
    boolean: { icon: ToggleRight, label: "Boolean" },
    enum: { icon: ListChecks, label: "Enum" },
    relation: { icon: Link2, label: "Relation" },
    library: { icon: FolderOpen, label: "Library" },
  };

  const logicOptions: { value: CollectionRuleGroup["operator"]; label: string }[] = [
    { value: "and", label: "All" },
    { value: "or", label: "Any" },
    { value: "not", label: "None" },
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
          value: defaultValue(field, field.operators[0]),
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
      entityTypes: field.entityTypes,
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

  function isNullaryOp(op: CollectionOperator): boolean {
    return op === "is_null" || op === "is_not_null" || op === "is_true" || op === "is_false";
  }

  function defaultValue(field: CollectionRuleFieldDef, operator: CollectionOperator): CollectionConditionValue {
    if (isNullaryOp(operator)) return null;
    if (operator === "between") return field.fieldType === "date" ? ["", ""] : [0, 0];
    if (operator === "in" || operator === "not_in") return [];
    if (field.fieldType === "number") return 0;
    if (field.fieldType === "boolean") return true;
    return "";
  }

  function toggleEntityType(index: number, kind: CollectionEntityType) {
    const condition = conditions[index];
    const field = fieldFor(condition);
    if (!fieldSupportsKind(field, kind)) return;

    const current = condition.entityTypes;
    updateCondition(index, {
      entityTypes: current.includes(kind)
        ? current.filter((item) => item !== kind)
        : [...current, kind],
    });
  }

  function fieldSupportsKind(field: CollectionRuleFieldDef, kind: CollectionEntityType): boolean {
    return field.entityTypes.length === 0 || field.entityTypes.includes(kind);
  }

  function getStringValue(value: CollectionConditionValue): string {
    if (value === null || value === undefined) return "";
    if (Array.isArray(value)) return "";
    return String(value);
  }

  function getNumberValue(value: CollectionConditionValue): number {
    if (typeof value === "number") return value;
    if (typeof value === "string") {
      const n = Number(value);
      return Number.isFinite(n) ? n : 0;
    }
    return 0;
  }

  function getBetweenValues(value: CollectionConditionValue): [string, string] {
    if (Array.isArray(value) && value.length === 2) {
      return [String(value[0] ?? ""), String(value[1] ?? "")];
    }
    return ["", ""];
  }

  function getMultiValues(value: CollectionConditionValue): string {
    if (Array.isArray(value)) return (value as string[]).join(", ");
    return "";
  }

  function parseBetween(field: CollectionRuleFieldDef, min: string, max: string): CollectionConditionValue {
    if (field.fieldType === "date") {
      return [min, max];
    }
    return [Number(min) || 0, Number(max) || 0];
  }

  function parseMultiValue(raw: string): string[] {
    return raw
      .split(",")
      .map((part) => part.trim())
      .filter(Boolean);
  }

  const controlClasses = cn(
    "min-w-0 w-full appearance-none rounded-xs border border-border-subtle bg-surface-2",
    "h-8 px-2 pr-6 text-[0.75rem] text-text-primary",
    "shadow-[inset_0_1px_4px_rgba(0,0,0,0.25)] outline-none transition-colors",
    "focus:border-border-accent focus:shadow-[inset_0_1px_4px_rgba(0,0,0,0.25),0_0_0_1px_rgba(199, 201, 204,0.2)]",
    "disabled:cursor-not-allowed disabled:opacity-50",
  );

  const valueClasses = cn(
    "min-w-0 w-full rounded-xs border border-border-subtle bg-surface-2",
    "h-8 px-2 text-[0.75rem] text-text-primary",
    "shadow-[inset_0_1px_4px_rgba(0,0,0,0.25)] outline-none transition-colors placeholder:text-text-disabled",
    "focus:border-border-accent focus:shadow-[inset_0_1px_4px_rgba(0,0,0,0.25),0_0_0_1px_rgba(199, 201, 204,0.2)]",
    "disabled:cursor-not-allowed disabled:opacity-50",
  );
</script>

<div class="flex flex-col gap-3">
  <!-- Logic selector: segmented control reading as a sentence -->
  <div class="flex items-center gap-2.5 flex-wrap text-[0.78rem] text-text-secondary">
    <span class="font-medium text-text-primary">Match</span>
    <div
      role="radiogroup"
      aria-label="Rule combination logic"
      class="inline-flex rounded-sm border border-border-subtle bg-surface-2 p-0.5 shadow-[inset_0_1px_3px_rgba(0,0,0,0.25)]"
    >
      {#each logicOptions as opt (opt.value)}
        {@const active = rule.operator === opt.value}
        <button
          type="button"
          role="radio"
          aria-checked={active}
          {disabled}
          onclick={() => updateGroup({ operator: opt.value })}
          class={cn(
            "rounded-xs px-3 py-1 text-[0.7rem] font-semibold uppercase tracking-[0.12em] transition-all",
            "disabled:cursor-not-allowed disabled:opacity-50",
            active
              ? "bg-gradient-to-b from-accent-900/60 to-accent-950/60 text-text-accent shadow-[inset_0_0_0_1px_rgba(199, 201, 204,0.45),0_0_12px_rgba(199, 201, 204,0.18)]"
              : "text-text-disabled hover:text-text-muted",
          )}
        >
          {opt.label}
        </button>
      {/each}
    </div>
    <span>of these conditions</span>
    <span class="ml-auto text-[0.65rem] font-mono uppercase tracking-wider text-text-disabled">
      {conditions.length} {conditions.length === 1 ? "rule" : "rules"}
    </span>
  </div>

  <!-- Rule list -->
  {#if conditions.length > 0}
    <div class="flex flex-col gap-2">
      {#each conditions as condition, i (i)}
        {@const field = fieldFor(condition)}
        {@const isNullary = isNullaryOp(condition.operator)}
        {@const isBetween = condition.operator === "between"}
        {@const isMulti = condition.operator === "in" || condition.operator === "not_in"}
        {@const TypeMetaIcon = fieldTypeMeta[field.fieldType].icon}
        <article
          class="group rounded-sm border border-border-subtle bg-surface-1/50 overflow-hidden transition-colors hover:border-border-default focus-within:border-border-accent/60"
        >
          <!-- Controls row -->
          <div class="flex items-stretch gap-2 p-2 pr-1.5">
            <!-- Rule rail: number + type badge -->
            <div class="flex items-center gap-1.5 shrink-0 pl-0.5 pt-1">
              <span class="text-[0.6rem] font-mono text-text-disabled/60 tabular-nums select-none w-3 text-right">
                {i + 1}
              </span>
              <div
                title={fieldTypeMeta[field.fieldType].label}
                class="inline-flex h-6 w-6 items-center justify-center rounded-xs border border-border-accent/30 bg-accent-950/30 shadow-[inset_0_1px_2px_rgba(0,0,0,0.3)]"
              >
                <TypeMetaIcon class="h-3 w-3 text-text-accent" />
              </div>
            </div>

            <!-- Field + operator + value -->
            <div class="grid grid-cols-1 sm:grid-cols-[1.1fr_0.75fr_1.4fr] gap-1.5 flex-1 min-w-0 items-start">
              <!-- Field selector -->
              <div class="relative">
                <select
                  aria-label="Rule field"
                  value={condition.field}
                  {disabled}
                  onchange={(e) => setField(i, (e.currentTarget as HTMLSelectElement).value)}
                  class={controlClasses}
                >
                  {#each COLLECTION_RULE_FIELDS as option (option.field)}
                    <option value={option.field}>{option.label}</option>
                  {/each}
                </select>
                <ChevronDown class="pointer-events-none absolute right-1.5 top-1/2 -translate-y-1/2 h-2.5 w-2.5 text-text-muted" />
              </div>

              <!-- Operator selector -->
              <div class="relative">
                <select
                  aria-label="Rule operator"
                  value={condition.operator}
                  {disabled}
                  onchange={(e) => setOperator(i, (e.currentTarget as HTMLSelectElement).value)}
                  class={controlClasses}
                >
                  {#each field.operators as operator (operator)}
                    <option value={operator}>{operator.replaceAll("_", " ")}</option>
                  {/each}
                </select>
                <ChevronDown class="pointer-events-none absolute right-1.5 top-1/2 -translate-y-1/2 h-2.5 w-2.5 text-text-muted" />
              </div>

              <!-- Value editor (contextual) -->
              {#if isNullary}
                <div class="hidden sm:flex items-center h-8 px-2 text-[0.7rem] font-mono uppercase tracking-wide text-text-disabled/70 italic">
                  no value
                </div>
              {:else if isBetween}
                {@const [minVal, maxVal] = getBetweenValues(condition.value)}
                <div class="grid grid-cols-[1fr_auto_1fr] items-center gap-1.5">
                  <input
                    aria-label="Range minimum"
                    type={field.fieldType === "date" ? "date" : "number"}
                    value={minVal}
                    placeholder="min"
                    {disabled}
                    oninput={(e) => {
                      const next = (e.currentTarget as HTMLInputElement).value;
                      updateCondition(i, { value: parseBetween(field, next, maxVal) });
                    }}
                    class={valueClasses}
                  />
                  <span class="text-[0.65rem] font-mono uppercase text-text-disabled select-none">to</span>
                  <input
                    aria-label="Range maximum"
                    type={field.fieldType === "date" ? "date" : "number"}
                    value={maxVal}
                    placeholder="max"
                    {disabled}
                    oninput={(e) => {
                      const next = (e.currentTarget as HTMLInputElement).value;
                      updateCondition(i, { value: parseBetween(field, minVal, next) });
                    }}
                    class={valueClasses}
                  />
                </div>
              {:else if isMulti}
                <input
                  aria-label="Multi value (comma separated)"
                  value={getMultiValues(condition.value)}
                  placeholder={field.enumValues ? field.enumValues.join(", ") : "value, value, …"}
                  {disabled}
                  oninput={(e) =>
                    updateCondition(i, {
                      value: parseMultiValue((e.currentTarget as HTMLInputElement).value),
                    })}
                  class={valueClasses}
                />
              {:else if field.fieldType === "date"}
                <input
                  aria-label="Date value"
                  type="date"
                  value={getStringValue(condition.value)}
                  {disabled}
                  oninput={(e) =>
                    updateCondition(i, { value: (e.currentTarget as HTMLInputElement).value })}
                  class={valueClasses}
                />
              {:else if field.fieldType === "number"}
                <input
                  aria-label="Number value"
                  type="number"
                  value={getNumberValue(condition.value)}
                  placeholder="0"
                  {disabled}
                  oninput={(e) => {
                    const raw = (e.currentTarget as HTMLInputElement).value;
                    const num = Number(raw);
                    updateCondition(i, { value: Number.isFinite(num) ? num : 0 });
                  }}
                  class={valueClasses}
                />
              {:else if field.fieldType === "enum" && field.enumValues}
                <div class="relative">
                  <select
                    aria-label="Enum value"
                    value={getStringValue(condition.value)}
                    {disabled}
                    onchange={(e) =>
                      updateCondition(i, { value: (e.currentTarget as HTMLSelectElement).value })}
                    class={controlClasses}
                  >
                    <option value="" disabled>Select…</option>
                    {#each field.enumValues as enumVal (enumVal)}
                      <option value={enumVal}>{enumVal}</option>
                    {/each}
                  </select>
                  <ChevronDown class="pointer-events-none absolute right-1.5 top-1/2 -translate-y-1/2 h-2.5 w-2.5 text-text-muted" />
                </div>
              {:else if field.fieldType === "library"}
                <div class="relative">
                  <select
                    aria-label="Library value"
                    value={getStringValue(condition.value)}
                    disabled={disabled || libraryOptions.length === 0}
                    onchange={(e) =>
                      updateCondition(i, { value: (e.currentTarget as HTMLSelectElement).value })}
                    class={controlClasses}
                  >
                    <option value="" disabled>{libraryOptions.length > 0 ? "Choose library" : "No visible libraries"}</option>
                    {#each libraryOptions as option (option.value)}
                      <option value={option.value}>{option.label}</option>
                    {/each}
                  </select>
                  <ChevronDown class="pointer-events-none absolute right-1.5 top-1/2 -translate-y-1/2 h-2.5 w-2.5 text-text-muted" />
                </div>
              {:else}
                <input
                  aria-label="Text value"
                  type="text"
                  value={getStringValue(condition.value)}
                  placeholder="Value"
                  {disabled}
                  oninput={(e) =>
                    updateCondition(i, { value: (e.currentTarget as HTMLInputElement).value })}
                  class={valueClasses}
                />
              {/if}
            </div>

            <!-- Remove button -->
            <button
              type="button"
              aria-label="Remove condition"
              title="Remove condition"
              {disabled}
              onclick={() => removeCondition(i)}
              class={cn(
                "shrink-0 self-start inline-flex items-center justify-center w-8 h-8 rounded-xs text-text-disabled/50 transition-colors",
                "hover:text-error-text hover:bg-error-muted/10",
                "disabled:cursor-not-allowed disabled:opacity-50",
              )}
            >
              <X class="h-3.5 w-3.5" />
            </button>
          </div>

          <!-- Entity type toggles -->
          <div
            class="flex min-w-0 items-center gap-1 border-t border-border-subtle/40 bg-surface-1/30 py-1.5 pl-2.5 pr-0 md:pl-14"
          >
            <span class="mr-2 shrink-0 text-[0.58rem] font-semibold uppercase tracking-[0.14em] text-text-disabled/70">
              Apply to
            </span>
            <div
              class="scrollbar-hidden flex min-w-0 flex-1 items-center gap-1 overflow-x-auto pr-2.5 [-webkit-overflow-scrolling:touch]"
            >
              {#each entityKinds as kind (kind.value)}
                {@const kindSupported = fieldSupportsKind(field, kind.value)}
                {@const active = kindSupported && (condition.entityTypes.length === 0 || condition.entityTypes.includes(kind.value))}
                {@const KindIcon = kind.icon}
                <button
                  type="button"
                  disabled={disabled || !kindSupported}
                  aria-pressed={active}
                  aria-disabled={!kindSupported}
                  title={kind.label}
                  onclick={() => toggleEntityType(i, kind.value)}
                  class={cn(
                    "inline-flex shrink-0 items-center gap-1 rounded-xs px-1.5 py-0.5 text-[0.58rem] font-semibold uppercase tracking-wider border transition-all",
                    "disabled:cursor-not-allowed disabled:opacity-50",
                    !kindSupported
                      ? "border-transparent bg-transparent text-text-disabled/35"
                      : active
                      ? "border-border-accent/50 bg-accent-950/30 text-text-accent"
                      : "border-transparent bg-transparent text-text-disabled/60 hover:text-text-muted hover:border-border-subtle",
                  )}
                >
                  <KindIcon class="h-2.5 w-2.5" />
                  <span>{kind.label}</span>
                </button>
              {/each}
            </div>
          </div>
        </article>
      {/each}
    </div>
  {:else}
    <div class="rounded-sm border border-dashed border-border-subtle bg-surface-1/30 px-4 py-6 text-center">
      <p class="m-0 text-[0.78rem] text-text-muted">No conditions yet</p>
      <p class="m-0 mt-1 text-[0.7rem] text-text-disabled">Add a rule below to begin filtering</p>
    </div>
  {/if}

  <!-- Add condition -->
  <button
    type="button"
    {disabled}
    onclick={addCondition}
    class={cn(
      "self-start inline-flex items-center gap-1.5 rounded-sm border border-dashed border-border-subtle bg-transparent px-3 py-1.5 text-[0.72rem] text-text-muted transition-all",
      "hover:border-border-accent hover:text-text-accent hover:bg-accent-950/10",
      "disabled:cursor-not-allowed disabled:opacity-50",
    )}
  >
    <Plus class="h-3 w-3" />
    Add condition
  </button>
</div>
