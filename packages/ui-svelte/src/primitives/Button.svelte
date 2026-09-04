<script module lang="ts">
  import {
    Button as BaseButton,
    buttonVariants as baseVariants,
    type ButtonVariant as BaseVariant,
    type ButtonSize as BaseSize,
  } from "../components/ui/button";

  /** Existing action names map to the standard shadcn button variants. */
  export type ButtonVariant = NonNullable<BaseVariant> | "primary" | "danger";
  export type ButtonSize = NonNullable<BaseSize> | "md";

  export function buttonVariants({
    variant = "primary",
    size = "md",
  }: { variant?: ButtonVariant; size?: ButtonSize } = {}) {
    return baseVariants({
      variant: variant === "primary" ? "default" : variant === "danger" ? "destructive" : variant,
      size: size === "md" ? "default" : size,
    });
  }
</script>

<script lang="ts">
  import type { HTMLButtonAttributes } from "svelte/elements";
  import type { Snippet } from "svelte";
  import { cn } from "../lib/utils";

  interface Props extends Omit<HTMLButtonAttributes, "class"> {
    variant?: ButtonVariant;
    size?: ButtonSize;
    class?: string;
    children?: Snippet;
  }

  let { variant = "primary", size = "md", class: className, children, ...rest }: Props = $props();
</script>

<BaseButton
  variant={variant === "primary" ? "default" : variant === "danger" ? "destructive" : variant}
  size={size === "md" ? "default" : size}
  class={cn(className)}
  {...rest}
>
  {@render children?.()}
</BaseButton>
