<script lang="ts">
  import type { HTMLAttributes } from "svelte/elements";
  import type { Snippet } from "svelte";
  import { cn } from "../lib/utils";

  interface Props extends Omit<HTMLAttributes<HTMLDivElement>, "class" | "children"> {
    variant?: "panel" | "well" | "elevated";
    class?: string;
    children?: Snippet;
  }

  let { variant = "panel", class: className, children, ...rest }: Props = $props();
</script>

<div
  class={cn(
    variant === "panel" && "surface-panel",
    variant === "well" && "surface-well",
    variant === "elevated" && "surface-elevated",
    className,
  )}
  {...rest}
>
  {#if children}{@render children()}{/if}
</div>
