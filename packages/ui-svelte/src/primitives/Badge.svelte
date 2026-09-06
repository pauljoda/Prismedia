<script module lang="ts">
  import { Badge as BaseBadge, badgeVariants as baseVariants, type BadgeVariant as BaseVariant } from "../components/ui/badge";

  export type BadgeVariant = NonNullable<BaseVariant> | "accent";

  /** Existing default badges remain quiet; accent maps to the base's primary fill. */
  export function badgeVariants({ variant = "default" }: { variant?: BadgeVariant } = {}) {
    return baseVariants({ variant: variant === "default" ? "secondary" : variant === "accent" ? "default" : variant });
  }
</script>

<script lang="ts">
  import type { HTMLAttributes } from "svelte/elements";
  import type { Snippet } from "svelte";
  import { cn } from "../lib/utils";

  interface Props extends Omit<HTMLAttributes<HTMLSpanElement>, "class"> {
    variant?: BadgeVariant;
    class?: string;
    children?: Snippet;
  }

  let { variant = "default", class: className, children, ...rest }: Props = $props();
</script>

<BaseBadge variant={variant === "default" ? "secondary" : variant === "accent" ? "default" : variant} class={cn(className)} {...rest}>
  {@render children?.()}
</BaseBadge>
