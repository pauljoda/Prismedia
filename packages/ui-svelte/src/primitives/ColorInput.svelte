<script lang="ts">
  import type { HTMLInputAttributes } from "svelte/elements";
  import { cn } from "../lib/utils";

  interface Props extends Omit<HTMLInputAttributes, "class" | "type" | "value" | "oninput"> {
    value: string;
    class?: string;
    onValueChange?: (value: string) => void;
  }

  let { value, class: className, onValueChange, ...rest }: Props = $props();
</script>

<input
  type="color"
  {value}
  class={cn(
    "h-6 w-6 cursor-pointer rounded-full border border-border-default bg-transparent p-0.5",
    "transition-all duration-fast hover:border-border-accent-strong focus-visible:outline-none focus-visible:shadow-focus-accent",
    className,
  )}
  oninput={(event) => onValueChange?.(event.currentTarget.value)}
  {...rest}
/>
