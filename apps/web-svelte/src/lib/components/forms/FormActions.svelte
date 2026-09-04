<script lang="ts">
  import { Loader2, Save, XCircle } from "@lucide/svelte";
  import { Button, Alert, cn } from "@prismedia/ui-svelte";

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

<div class={cn("@container flex flex-col gap-2", fullWidth && "w-full")}>
  {#if error}
    <Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>
  {/if}
  <div class={cn("flex flex-wrap items-center gap-control-gap @max-[24rem]:grid @max-[24rem]:grid-cols-1", justify)}>
    <Button
      type="button"
      onclick={onCancel}
      disabled={saving}
      variant="outline"
      class={fullWidth ? "flex-1" : undefined}
    >
      <XCircle data-icon="inline-start" />
      {cancelLabel}
    </Button>
    <Button
      type="button"
      onclick={onSave}
      disabled={saving || saveDisabled}
      aria-label={saveLabel}
      variant="primary"
      class={fullWidth ? "flex-1" : undefined}
    >
      {#if saving}
        <Loader2 data-icon="inline-start" class="animate-spin motion-reduce:animate-none" />
      {:else}
        <Save data-icon="inline-start" />
      {/if}
      {saving ? "Saving…" : saveLabel}
    </Button>
  </div>
</div>
