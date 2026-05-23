<script module lang="ts">
  import { cva, type VariantProps } from "class-variance-authority";

  export const textInputVariants = cva(
    [
      "w-full bg-surface-1 text-text-primary border rounded-xs",
      "shadow-[inset_0_1px_3px_rgba(0,0,0,0.25)]",
      "placeholder:text-text-disabled",
      "transition-all duration-fast",
      "focus:outline-none focus:border-border-accent-strong focus:shadow-[var(--shadow-focus-accent)]",
      "disabled:opacity-40 disabled:cursor-not-allowed",
    ].join(" "),
    {
      variants: {
        size: {
          sm: "h-8 px-2.5 text-xs",
          md: "h-9 px-3 text-sm",
          lg: "h-10 px-3.5 text-sm",
        },
        variant: {
          default: "border-border-default",
          error: "border-error/40",
        },
      },
      defaultVariants: {
        size: "md",
        variant: "default",
      },
    },
  );

  export type TextInputSize = NonNullable<VariantProps<typeof textInputVariants>["size"]>;
  export type TextInputVariant = NonNullable<VariantProps<typeof textInputVariants>["variant"]>;
</script>

<script lang="ts">
  import type { HTMLInputAttributes } from "svelte/elements";
  import { cn } from "../lib/utils";

  interface Props extends Omit<HTMLInputAttributes, "class" | "size"> {
    size?: TextInputSize;
    variant?: TextInputVariant;
    class?: string;
  }

  let {
    size = "md",
    variant = "default",
    class: className,
    type = "text",
    ...rest
  }: Props = $props();
</script>

<input {type} class={cn(textInputVariants({ size, variant }), className)} {...rest} />
