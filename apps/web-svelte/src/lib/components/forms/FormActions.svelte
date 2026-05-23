<script lang="ts">
  import { Loader2, Save, XCircle } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    onSave: () => void;
    onCancel: () => void;
    saving?: boolean;
    saveDisabled?: boolean;
    saveLabel?: string;
    cancelLabel?: string;
    error?: string | null;
    align?: "start" | "end" | "between";
    fullWidth?: boolean;
  }

  let {
    onSave,
    onCancel,
    saving = false,
    saveDisabled = false,
    saveLabel = "Save",
    cancelLabel = "Cancel",
    error = null,
    align = "end",
    fullWidth = false,
  }: Props = $props();

  const justify = $derived(
    align === "between" ? "justify-between" : align === "start" ? "justify-start" : "justify-end",
  );
</script>

<div class={cn("flex flex-col gap-2", fullWidth && "w-full")}>
  {#if error}
    <p class="text-[0.72rem] text-error-text">{error}</p>
  {/if}
  <div class={cn("flex items-center gap-2", justify)}>
    <button
      type="button"
      onclick={onCancel}
      disabled={saving}
      class={cn(
        "inline-flex items-center gap-1.5 border border-border-subtle bg-surface-2 px-3 py-2 text-[0.78rem] text-text-muted transition-colors",
        "hover:border-border-default hover:text-text-primary",
        "disabled:cursor-not-allowed disabled:opacity-50",
        fullWidth && "flex-1 justify-center",
      )}
    >
      <XCircle class="h-3.5 w-3.5" />
      {cancelLabel}
    </button>
    <button
      type="button"
      onclick={onSave}
      disabled={saving || saveDisabled}
      aria-label={saveLabel}
      class={cn(
        "inline-flex items-center gap-1.5 border border-border-accent bg-gradient-to-r from-accent-900 via-accent-800 to-accent-900 px-4 py-2 text-[0.78rem] font-medium text-accent-100 shadow-[var(--shadow-glow-accent)] transition-all",
        "hover:shadow-[var(--shadow-glow-accent-strong)]",
        "disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none",
        fullWidth && "flex-1 justify-center",
      )}
    >
      {#if saving}
        <Loader2 class="h-3.5 w-3.5 animate-spin" />
      {:else}
        <Save class="h-3.5 w-3.5" />
      {/if}
      {saving ? "Saving…" : saveLabel}
    </button>
  </div>
</div>
