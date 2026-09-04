<script lang="ts">
  import { Loader2, Search, X } from "@lucide/svelte";
  import type { HTMLInputAttributes } from "svelte/elements";
  import * as InputGroup from "../components/ui/input-group";
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

<InputGroup.Root class={className} aria-disabled={rest.disabled}>
  <InputGroup.Addon>
    <Search class={cn("size-4", searchIconClass)} aria-hidden="true" />
  </InputGroup.Addon>
  <InputGroup.Input
    bind:ref={element}
    bind:value
    type="search"
    aria-label={ariaLabel}
    class={cn("[&::-webkit-search-cancel-button]:appearance-none [&::-webkit-search-decoration]:appearance-none", inputClass)}
    {...rest}
  />
  {#if (clearable && value) || loading}
    <InputGroup.Addon align="inline-end">
      {#if clearable && value}
        <InputGroup.Button
          size="icon-xs"
          class={clearButtonClass}
          disabled={rest.disabled}
          onclick={clear}
          aria-label={clearLabel}
          title={clearLabel}
        >
          <X class={cn("size-3.5", clearIconClass)} />
        </InputGroup.Button>
      {/if}
      {#if loading}
        <Loader2 class="size-3.5 shrink-0 animate-spin text-muted-foreground motion-reduce:animate-none" aria-label="Searching" />
      {/if}
    </InputGroup.Addon>
  {/if}
</InputGroup.Root>
