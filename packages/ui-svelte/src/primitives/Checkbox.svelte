<script lang="ts">
  import type { ComponentProps } from "svelte";
  import BaseCheckbox from "../components/ui/checkbox/checkbox.svelte";
  import { cn } from "../lib/utils";
  interface Props extends Omit<ComponentProps<typeof BaseCheckbox>, "onCheckedChange" | "onchange"> {
    size?: "sm" | "md";
    onchange?: (checked: boolean) => void;
  }
  let { checked = false, indeterminate = false, onchange, size = "sm", class: className, ...rest }: Props = $props();
</script>

<BaseCheckbox {...rest} bind:checked={() => checked, (next) => onchange?.(next)}
  {indeterminate} class={cn(size === "sm" ? "size-3.5" : "size-4", className)} />
