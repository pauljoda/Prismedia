<script module lang="ts">
  import { cva, type VariantProps } from "class-variance-authority";

  export const buttonVariants = cva(
    [
      "inline-flex items-center justify-center gap-2 font-medium rounded-sm",
      "transition-all duration-fast",
      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-500/20 focus-visible:ring-offset-1 focus-visible:ring-offset-bg",
      "disabled:pointer-events-none disabled:opacity-40",
    ].join(" "),
    {
      variants: {
        variant: {
          primary: [
            "border border-accent-200 bg-accent-200 text-accent-950",
            "shadow-[0_1px_3px_rgba(0,0,0,0.32)]",
            "hover:border-accent-50 hover:bg-accent-50 hover:shadow-[0_2px_7px_rgba(0,0,0,0.36)]",
            "active:border-accent-400 active:bg-accent-400 active:shadow-[inset_0_1px_2px_rgba(0,0,0,0.18)]",
          ].join(" "),
          secondary: [
            "surface-card text-text-secondary",
            "hover:text-text-primary hover:border-border-accent",
          ].join(" "),
          ghost: [
            "text-text-muted bg-transparent",
            "hover:text-text-primary hover:bg-surface-2",
          ].join(" "),
          danger: [
            "bg-error-muted text-error-text border border-error/20",
            "hover:bg-error/10 hover:text-error-text",
          ].join(" "),
        },
        size: {
          sm: "h-7 px-2.5 text-xs",
          md: "h-8 px-3.5 text-sm",
          lg: "h-10 px-5 text-sm",
          icon: "h-8 w-8",
        },
      },
      defaultVariants: {
        variant: "primary",
        size: "md",
      },
    },
  );

  export type ButtonVariant = NonNullable<VariantProps<typeof buttonVariants>["variant"]>;
  export type ButtonSize = NonNullable<VariantProps<typeof buttonVariants>["size"]>;
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

  let {
    variant = "primary",
    size = "md",
    class: className,
    children,
    type = "button",
    ...rest
  }: Props = $props();
</script>

<button {type} class={cn(buttonVariants({ variant, size }), className)} {...rest}>
  {#if children}{@render children()}{/if}
</button>
