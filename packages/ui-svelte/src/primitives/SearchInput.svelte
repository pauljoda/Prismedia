<script lang="ts">
  import { Loader2, Search, X } from "@lucide/svelte";
  import type { HTMLInputAttributes } from "svelte/elements";
  import { cn } from "../lib/utils";

  interface Props extends Omit<HTMLInputAttributes, "class" | "type" | "value"> {
    value?: string;
    element?: HTMLInputElement | null;
    ariaLabel: string;
    clearLabel?: string;
    onClear?: () => void;
    loading?: boolean;
    clearable?: boolean;
    class?: string;
    searchIconClass?: string;
    inputClass?: string;
    clearButtonClass?: string;
    clearIconClass?: string;
  }

  let {
    value = $bindable(""),
    element = $bindable(null),
    ariaLabel,
    clearLabel = "Clear search",
    onClear,
    loading = false,
    clearable = true,
    class: className,
    searchIconClass,
    inputClass,
    clearButtonClass,
    clearIconClass,
    ...rest
  }: Props = $props();

  function clear() {
    value = "";
    onClear?.();
    queueMicrotask(() => element?.focus());
  }
</script>

<div class={cn("surface-well flex items-center gap-2 px-3 py-2", className)}>
  <Search class={cn("h-4 w-4 shrink-0 text-text-disabled", searchIconClass)} aria-hidden="true" />
  <input
    bind:this={element}
    bind:value
    type="search"
    aria-label={ariaLabel}
    class={cn(
      "min-w-0 flex-1 bg-transparent text-sm text-text-primary placeholder:text-text-disabled focus:outline-none",
      inputClass,
    )}
    {...rest}
  />
  {#if clearable && value}
    <button
      type="button"
      class={cn(
        "flex h-7 w-7 shrink-0 items-center justify-center rounded-xs text-text-disabled transition-colors duration-fast hover:bg-surface-2 hover:text-text-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-500/20",
        clearButtonClass,
      )}
      onclick={clear}
      aria-label={clearLabel}
      title={clearLabel}
    >
      <X class={cn("h-3.5 w-3.5", clearIconClass)} />
    </button>
  {/if}
  {#if loading}
    <Loader2 class="h-3.5 w-3.5 shrink-0 animate-spin text-text-disabled" aria-label="Searching" />
  {/if}
</div>
