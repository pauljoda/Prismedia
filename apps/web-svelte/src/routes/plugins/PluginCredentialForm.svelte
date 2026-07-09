<script lang="ts">
  import { Loader2, Save } from "@lucide/svelte";
  import { Button, TextInput } from "@prismedia/ui-svelte";
  import type { PluginAuthField } from "$lib/api/generated/model";
  import { authLinkLabel } from "./plugin-auth-format";

  interface Props {
    fields: PluginAuthField[];
    getPlaceholder: (field: PluginAuthField) => string;
    getValueKey: (field: PluginAuthField) => string;
    inputIdPrefix: string;
    onCancel: () => void;
    onSave: () => void;
    saving: boolean;
    values: Record<string, string>;
  }

  let {
    fields,
    getPlaceholder,
    getValueKey,
    inputIdPrefix,
    onCancel,
    onSave,
    saving,
    values = $bindable(),
  }: Props = $props();

  const canSave = $derived(fields.some((field) => values[getValueKey(field)]?.trim()));

  function updateValue(field: PluginAuthField, value: string) {
    values = {
      ...values,
      [getValueKey(field)]: value,
    };
  }
</script>

<div class="border-t border-border-subtle px-4 py-3 space-y-3 bg-surface-1/50">
  <h4 class="text-[0.72rem] font-medium text-text-secondary">Authentication</h4>
  {#each fields as field (field.key)}
    {@const valueKey = getValueKey(field)}
    <div>
      <div class="flex items-center justify-between mb-1">
        <label class="text-[0.65rem] text-text-disabled" for="{inputIdPrefix}-{field.key}">
          {field.label}
          {#if field.required}
            <span class="text-status-error-text ml-0.5">*</span>
          {/if}
        </label>
        {#if field.url}
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onclick={() => window.open(field.url ?? "", "_blank", "noopener,noreferrer")}
            class="h-auto p-0 text-[0.6rem] text-text-accent hover:bg-transparent hover:underline"
          >
            {authLinkLabel(field)}
          </Button>
        {/if}
      </div>
      <TextInput
        id="{inputIdPrefix}-{field.key}"
        type="password"
        size="sm"
        value={values[valueKey] ?? ""}
        oninput={(event) => updateValue(field, event.currentTarget.value)}
        placeholder={getPlaceholder(field)}
        class="font-mono"
      />
    </div>
  {/each}
  <div class="flex items-center justify-end gap-2 pt-1">
    <Button
      type="button"
      variant="ghost"
      size="sm"
      onclick={onCancel}
      class="h-auto px-3 py-1.5 text-[0.72rem]"
    >
      Cancel
    </Button>
    <Button
      type="button"
      variant="primary"
      size="sm"
      disabled={saving || !canSave}
      onclick={onSave}
      class="h-auto gap-1.5 px-3 py-1.5 text-[0.72rem]"
    >
      {#if saving}
        <Loader2 class="h-3 w-3 animate-spin" />
      {:else}
        <Save class="h-3 w-3" />
      {/if}
      Save Credentials
    </Button>
  </div>
</div>
