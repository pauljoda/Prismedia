<script lang="ts">
  import { Plus, X } from "@lucide/svelte";
  import { Button, ChoiceGroup, Field } from "@prismedia/ui-svelte";
  import { COLLECTION_RULE_GROUP_OPERATOR } from "$lib/api/generated/codes";
  import { COLLECTION_RULE_FIELDS, type CollectionRuleGroup, type CollectionRuleNode } from "$lib/collections/models";
  import { defaultConditionValue } from "$lib/collections/rule-editor";
  import ConditionEditor from "./ConditionEditor.svelte";
  import Self from "./ConditionBuilder.svelte";

  interface Props {
    rule: CollectionRuleGroup;
    onChange: (rule: CollectionRuleGroup) => void;
    disabled?: boolean;
    libraryOptions?: { value: string; label: string }[];
  }

  let { rule, onChange, disabled = false, libraryOptions = [] }: Props = $props();
  const logicOptions = [
    { value: COLLECTION_RULE_GROUP_OPERATOR.and, label: "All" },
    { value: COLLECTION_RULE_GROUP_OPERATOR.or, label: "Any" },
    { value: COLLECTION_RULE_GROUP_OPERATOR.not, label: "None" },
  ];

  function replaceChild(index: number, child: CollectionRuleNode) {
    onChange({ ...rule, children: rule.children.map((current, i) => i === index ? child : current) });
  }

  function removeChild(index: number) {
    onChange({ ...rule, children: rule.children.filter((_, i) => i !== index) });
  }

  function addCondition() {
    const field = COLLECTION_RULE_FIELDS[0];
    onChange({ ...rule, children: [...rule.children, {
      type: "condition", entityTypes: [], field: field.field,
      operator: field.operators[0], value: defaultConditionValue(field, field.operators[0]),
    }] });
  }
</script>

<Field.Group>
  <Field.Field>
    <Field.Title>Match conditions</Field.Title>
    <ChoiceGroup type="single" options={logicOptions} value={rule.operator}
      ariaLabel="Rule combination logic" {disabled}
      onValueChange={(operator) => onChange({ ...rule, operator })} />
  </Field.Field>

  <!-- Rules have no persistent row IDs. Positional components retain focus during immutable edits. -->
  {#each rule.children as child, index}
    {#if child.type === "group"}
      <Field.Set class="min-w-0 rounded-lg border border-border-subtle p-4">
        <Field.Legend variant="label">Condition group</Field.Legend>
        <Self rule={child} {disabled} {libraryOptions} onChange={(next) => replaceChild(index, next)} />
        <Button variant="ghost" class="self-end" {disabled} onclick={() => removeChild(index)}>
          <X data-icon="inline-start" />Remove group
        </Button>
      </Field.Set>
    {:else}
      <ConditionEditor condition={child} {disabled} {libraryOptions}
        onChange={(next) => replaceChild(index, next)} onRemove={() => removeChild(index)} />
    {/if}
  {/each}

  <Button variant="outline" {disabled} onclick={addCondition} class="self-start">
    <Plus data-icon="inline-start" />Add condition
  </Button>
</Field.Group>
