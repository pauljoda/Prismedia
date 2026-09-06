<script lang="ts">
  import { ChevronRight, X } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import { useIdentifyStore } from "./identify-store.svelte";

  interface Props {
    entityId: string;
    showNext?: boolean;
    disabled?: boolean;
    compact?: boolean;
    class?: string;
  }

  let {
    entityId,
    showNext = false,
    disabled = false,
    compact = false,
    class: className = "",
  }: Props = $props();

  const store = useIdentifyStore();
  const buttonSize = $derived(
    compact ? "h-8 px-2.5 text-[0.76rem]" : "h-10 px-3 text-[0.78rem] md:h-9",
  );
  const buttonDisabled = $derived(disabled || store.applying);
  const dangerButtonClass =
    "border-error/40 bg-error-muted/20 text-error-text shadow-[0_0_14px_rgba(168,72,80,0.08)] hover:border-error/70 hover:bg-error-muted/40 disabled:cursor-not-allowed disabled:opacity-40";
</script>

<div class={cn("flex w-full flex-col gap-2 md:w-auto md:flex-row md:items-center md:gap-3", className)}>
  <Button variant="outline" size="sm"
    type="button"
    class={cn(
      "inline-flex w-full items-center justify-center gap-1.5 rounded-xs border font-medium transition-colors md:w-auto",
      buttonSize,
      dangerButtonClass,
    )}
    disabled={buttonDisabled}
    onclick={() => void store.rejectQueueItem(entityId)}
  >
    <X class="h-3.5 w-3.5" />
    Reject
  </Button>
  {#if showNext}
    <Button variant="outline" size="sm"
      type="button"
      class={cn(
        "inline-flex w-full items-center justify-center gap-1.5 rounded-xs border font-medium transition-colors md:w-auto",
        buttonSize,
        dangerButtonClass,
      )}
      disabled={buttonDisabled}
      onclick={() => void store.rejectQueueItem(entityId, { navigateNext: true })}
    >
      <X class="h-3.5 w-3.5" />
      Reject and Next
      <ChevronRight class="h-3.5 w-3.5" />
    </Button>
  {/if}
</div>
