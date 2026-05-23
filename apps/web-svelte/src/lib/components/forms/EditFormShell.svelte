<script lang="ts">
  import type { Snippet } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
  import FormActions from "./FormActions.svelte";

  interface Props {
    onSave: () => void;
    onCancel: () => void;
    saving?: boolean;
    saveDisabled?: boolean;
    saveLabel?: string;
    cancelLabel?: string;
    error?: string | null;
    title?: string;
    description?: string;
    class?: string;
    children: Snippet;
    /** Optional snippet for an inline header trailing slot (e.g. a status chip). */
    headerEnd?: Snippet;
  }

  let {
    onSave,
    onCancel,
    saving = false,
    saveDisabled = false,
    saveLabel = "Save",
    cancelLabel = "Cancel",
    error = null,
    title,
    description,
    class: className = "",
    children,
    headerEnd,
  }: Props = $props();
</script>

<form
  class={cn("surface-panel p-5 space-y-5", className)}
  onsubmit={(e) => {
    e.preventDefault();
    if (!saving && !saveDisabled) onSave();
  }}
>
  {#if title || description || headerEnd}
    <header class="flex items-start justify-between gap-3">
      <div class="min-w-0 space-y-1">
        {#if title}
          <h2 class="text-kicker">{title}</h2>
        {/if}
        {#if description}
          <p class="text-[0.78rem] text-text-muted">{description}</p>
        {/if}
      </div>
      {#if headerEnd}
        <div class="flex-shrink-0">{@render headerEnd()}</div>
      {/if}
    </header>
  {/if}

  <div class="space-y-4">
    {@render children()}
  </div>

  <div class="border-t border-border-subtle/60 pt-4">
    <FormActions
      {onSave}
      {onCancel}
      {saving}
      {saveDisabled}
      {saveLabel}
      {cancelLabel}
      {error}
    />
  </div>
</form>
