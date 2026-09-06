<script lang="ts">
  import type { HTMLAttributes } from "svelte/elements";
  import type { Snippet } from "svelte";
  import { cn } from "../lib/utils";
  import { Root as Card } from "../components/ui/card";

  interface Props extends Omit<HTMLAttributes<HTMLDivElement>, "class" | "children"> {
    variant?: "panel" | "well" | "elevated";
    class?: string;
    children?: Snippet;
  }

  let { variant = "panel", class: className, children, ...rest }: Props = $props();
</script>

<Card
  class={cn(
    "gap-0 py-0",
    variant === "well" && "bg-muted",
    variant === "elevated" && "bg-surface-2",
    className,
  )}
  {...rest}
>
  {#if children}{@render children()}{/if}
</Card>
