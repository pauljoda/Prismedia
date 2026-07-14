<script module lang="ts">
  import { cva, type VariantProps } from "class-variance-authority";

  export const badgeVariants = cva(
    "inline-flex items-center px-2 py-0.5 text-xs font-medium border rounded-xs",
    {
      variants: {
        variant: {
          default: "bg-surface-2 border-border-subtle text-text-secondary",
          accent:
            "bg-gradient-to-r from-accent-950 to-accent-900 border-accent-500/25 text-accent-300 shadow-[inset_0_1px_0_rgba(199, 201, 204,0.08),0_0_8px_rgba(199, 201, 204,0.06)]",
          success: "bg-success-muted/30 border-success/20 text-success-text",
          warning: "bg-warning-muted/30 border-warning/20 text-warning-text",
          error: "bg-error-muted/30 border-error/20 text-error-text",
          info: "bg-info-muted/30 border-info/20 text-info-text",
        },
      },
      defaultVariants: {
        variant: "default",
      },
    },
  );

  export type BadgeVariant = NonNullable<VariantProps<typeof badgeVariants>["variant"]>;
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

<span class={cn(badgeVariants({ variant }), className)} {...rest}>
  {#if children}{@render children()}{/if}
</span>
