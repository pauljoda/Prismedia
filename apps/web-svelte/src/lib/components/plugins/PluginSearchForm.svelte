<script lang="ts">
  import { Loader2, Search, X } from "@lucide/svelte";
  import { Button, TextInput } from "@prismedia/ui-svelte";
  import { PLUGIN_SEARCH_FIELD_TYPE } from "$lib/api/generated/codes";
  import type { PluginSearchField } from "$lib/api/generated/model";

  interface Props {
    fields: PluginSearchField[];
    values: Record<string, string>;
    onValuesChange: (values: Record<string, string>) => void;
    onSubmit: () => void;
    onClear: () => void;
    loading?: boolean;
    disabled?: boolean;
    submitDisabled?: boolean;
    submitLabel?: string;
  }

  let {
    fields,
    values,
    onValuesChange,
    onSubmit,
    onClear,
    loading = false,
    disabled = false,
    submitDisabled = false,
    submitLabel = "Search",
  }: Props = $props();

  function inputType(field: PluginSearchField): "text" | "number" {
    return field.type === PLUGIN_SEARCH_FIELD_TYPE.text ? "text" : "number";
  }

  function update(key: string, value: string) {
    onValuesChange({ ...values, [key]: value });
  }
</script>

<form
  class="grid grid-cols-1 gap-3 md:grid-cols-[repeat(auto-fit,minmax(10rem,1fr))_auto] md:items-end"
  onsubmit={(event) => {
    event.preventDefault();
    if (!disabled && !submitDisabled && !loading) onSubmit();
  }}
>
  {#each fields as field (field.key)}
    <label class="flex min-w-0 flex-col gap-1.5">
      <span class="flex items-baseline gap-1.5 font-mono text-[0.72rem] text-text-muted">
        {field.label}
        {#if field.required}<span class="text-text-accent" aria-hidden="true">*</span>{/if}
      </span>
      <TextInput
        type={inputType(field)}
        value={values[field.key] ?? ""}
        required={field.required}
        min={field.type === PLUGIN_SEARCH_FIELD_TYPE.year ? 1000 : undefined}
        max={field.type === PLUGIN_SEARCH_FIELD_TYPE.year ? 9999 : undefined}
        step={field.type === PLUGIN_SEARCH_FIELD_TYPE.year ? 1 : undefined}
        placeholder={field.placeholder ?? undefined}
        disabled={disabled || loading}
        aria-label={field.label}
        oninput={(event) => update(field.key, event.currentTarget.value)}
      />
      {#if field.help}
        <span class="text-[0.68rem] leading-snug text-text-disabled">{field.help}</span>
      {/if}
    </label>
  {/each}

  <div class="flex flex-col gap-2 sm:flex-row md:self-end">
    <Button type="button" variant="secondary" disabled={disabled || loading} class="gap-1.5" onclick={onClear}>
      <X class="h-3.5 w-3.5" />
      Clear
    </Button>
    <Button type="submit" variant="primary" disabled={disabled || submitDisabled || loading} class="gap-1.5">
      {#if loading}
        <Loader2 class="h-3.5 w-3.5 animate-spin" />
      {:else}
        <Search class="h-3.5 w-3.5" />
      {/if}
      {submitLabel}
    </Button>
  </div>
</form>
