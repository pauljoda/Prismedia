<script lang="ts">
  import { Loader2, Search, SlidersHorizontal, X } from "@lucide/svelte";
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
    /** Lead with required fields; optional refinements stay available on demand. */
    compact?: boolean;
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
    compact = false,
  }: Props = $props();

  let expanded = $state(false);
  const hasOptionalFields = $derived(fields.some(field => !field.required));
  const visibleFields = $derived(compact && !expanded ? fields.filter((field, index) => field.required || index === 0) : fields);

  function inputType(field: PluginSearchField): "text" | "number" {
    return field.type === PLUGIN_SEARCH_FIELD_TYPE.text ? "text" : "number";
  }

  function update(key: string, value: string) {
    onValuesChange({ ...values, [key]: value });
  }
</script>

<form
  class={`grid grid-cols-1 gap-3 sm:items-end ${compact && visibleFields.length === 1 ? "sm:grid-cols-[minmax(0,1fr)_auto]" : "sm:grid-cols-2 xl:grid-cols-3"}`}
  onsubmit={(event) => {
    event.preventDefault();
    if (!disabled && !submitDisabled && !loading) onSubmit();
  }}
>
  {#each visibleFields as field (field.key)}
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

  <div class="flex flex-wrap items-center gap-2 sm:self-end">
    {#if compact && hasOptionalFields}<Button type="button" variant="ghost" disabled={disabled || loading} aria-expanded={expanded} onclick={() => expanded = !expanded}><SlidersHorizontal />{expanded ? "Fewer filters" : "More filters"}</Button>{/if}
    <Button type="button" variant="secondary" disabled={disabled || loading} class="gap-1.5" aria-label="Clear" onclick={onClear}>
      <X class="h-3.5 w-3.5" />
      {#if !compact}Clear{/if}
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
