<script lang="ts">
  import { Plus, FolderPlus, Trash2 } from "@lucide/svelte";
  import type {
    CollectionRuleGroup,
    CollectionRuleCondition,
    CollectionRuleNode,
  } from "@prismedia/contracts";
  import { COLLECTION_RULE_FIELDS } from "@prismedia/contracts";
  import ConditionRow from "./ConditionRow.svelte";
  import Self from "./ConditionGroup.svelte";
  import type { SuggestionItem } from "$lib/collection-suggestions";

  interface Props {
    group: CollectionRuleGroup;
    onChange: (group: CollectionRuleGroup) => void;
    onDelete?: () => void;
    depth?: number;
    availableTags?: SuggestionItem[];
    availablePerformers?: SuggestionItem[];
    availableStudios?: SuggestionItem[];
  }

  let {
    group,
    onChange,
    onDelete,
    depth = 0,
    availableTags = [],
    availablePerformers = [],
    availableStudios = [],
  }: Props = $props();

  function newCondition(): CollectionRuleCondition {
    const defaultField = COLLECTION_RULE_FIELDS[0];
    return {
      type: "condition",
      entityTypes: [],
      field: defaultField.field,
      operator: defaultField.operators[0],
      value: null,
    };
  }

  function newGroup(): CollectionRuleGroup {
    return { type: "group", operator: "and", children: [newCondition()] };
  }

  const borderColor = $derived(
    depth === 0
      ? "border-accent-brass/30"
      : depth === 1
        ? "border-accent-brass/20"
        : "border-border-default",
  );

  function handleChildChange(index: number, child: CollectionRuleNode) {
    const newChildren = [...group.children];
    newChildren[index] = child;
    onChange({ ...group, children: newChildren });
  }

  function handleChildDelete(index: number) {
    const newChildren = group.children.filter((_, i) => i !== index);
    if (newChildren.length === 0 && onDelete) onDelete();
    else onChange({ ...group, children: newChildren });
  }

  function handleAddCondition() {
    onChange({ ...group, children: [...group.children, newCondition()] });
  }

  function handleAddGroup() {
    onChange({ ...group, children: [...group.children, newGroup()] });
  }
</script>

<div class={`relative border-l-2 ${borderColor} pl-3 py-2 space-y-2`}>
  <div class="flex items-center gap-2">
    <div class="flex items-center border border-border-default">
      {#each ["and", "or", "not"] as const as op (op)}
        <button
          type="button"
          onclick={() => onChange({ ...group, operator: op })}
          class={`px-2 py-0.5 text-[0.7rem] font-mono uppercase transition-colors ${
            group.operator === op
              ? "bg-accent-brass/15 text-text-accent"
              : "text-text-muted hover:text-text-secondary"
          }`}
        >
          {op}
        </button>
      {/each}
    </div>

    <span class="text-[0.65rem] text-text-disabled">
      {group.operator === "and"
        ? "All conditions must match"
        : group.operator === "or"
          ? "Any condition matches"
          : "None of the conditions match"}
    </span>

    {#if onDelete}
      <button
        type="button"
        onclick={onDelete}
        class="ml-auto p-1 text-text-disabled hover:text-error-text transition-colors"
        aria-label="Delete group"
      >
        <Trash2 class="h-3.5 w-3.5" />
      </button>
    {/if}
  </div>

  {#each group.children as child, index (index)}
    <div>
      {#if child.type === "condition"}
        <ConditionRow
          condition={child}
          onChange={(updated) => handleChildChange(index, updated)}
          onDelete={() => handleChildDelete(index)}
          {availableTags}
          {availablePerformers}
          {availableStudios}
        />
      {:else}
        <Self
          group={child as CollectionRuleGroup}
          onChange={(updated) => handleChildChange(index, updated)}
          onDelete={() => handleChildDelete(index)}
          depth={depth + 1}
          {availableTags}
          {availablePerformers}
          {availableStudios}
        />
      {/if}
    </div>
  {/each}

  <div class="flex items-center gap-1.5 pt-1">
    <button
      type="button"
      onclick={handleAddCondition}
      class="inline-flex items-center gap-1 px-2 py-1 text-[0.7rem] text-text-muted hover:text-text-accent bg-surface-2 hover:bg-surface-3 transition-colors"
    >
      <Plus class="h-3 w-3" />
      Condition
    </button>
    {#if depth < 3}
      <button
        type="button"
        onclick={handleAddGroup}
        class="inline-flex items-center gap-1 px-2 py-1 text-[0.7rem] text-text-muted hover:text-text-accent bg-surface-2 hover:bg-surface-3 transition-colors"
      >
        <FolderPlus class="h-3 w-3" />
        Group
      </button>
    {/if}
  </div>
</div>
