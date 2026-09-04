<script module lang="ts">
  import { cva, type VariantProps } from "class-variance-authority";
  import Input, { inputStyles } from "../components/ui/input/input.svelte";

  export const textInputVariants = cva(inputStyles, {
    variants: {
      size: { sm: "h-8 px-2.5 text-xs", md: "h-9 px-3 text-sm", lg: "h-10 px-3.5 text-sm" },
      variant: { default: "", error: "border-destructive" },
    },
    defaultVariants: { size: "md", variant: "default" },
  });
  export type TextInputSize = NonNullable<VariantProps<typeof textInputVariants>["size"]>;
  export type TextInputVariant = NonNullable<VariantProps<typeof textInputVariants>["variant"]>;
</script>

<script lang="ts">
  import type { HTMLInputAttributes } from "svelte/elements";
  import { cn } from "../lib/utils";

  interface Props extends Omit<HTMLInputAttributes, "class" | "size"> {
    /** Native input reference for composing focus behavior. */
    ref?: HTMLInputElement | null;
    size?: TextInputSize;
    variant?: TextInputVariant;
    class?: string;
  }

  let {
    ref = $bindable(null),
    value = $bindable(),
    size = "md",
    variant = "default",
    class: className,
    type = "text",
    ...rest
  }: Props = $props();
</script>

<Input
  bind:ref
  bind:value
  type={type ?? "text"}
  aria-invalid={variant === "error" ? "true" : undefined}
  class={cn(textInputVariants({ size, variant }), className)}
  {...rest}
/>
