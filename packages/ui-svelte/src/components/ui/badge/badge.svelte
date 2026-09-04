<script lang="ts" module>
	import { type VariantProps, cva } from "class-variance-authority";

	export const badgeVariants = cva("min-h-badge gap-control-gap-sm rounded-xs border border-transparent px-2 py-0.5 text-caption font-medium transition-all has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg]:size-icon-sm! group/badge inline-flex w-fit shrink-0 items-center justify-center overflow-hidden whitespace-nowrap transition-colors focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/30 aria-invalid:border-destructive aria-invalid:ring-destructive/20 [&>svg]:pointer-events-none", {
		variants: {
			variant: {
				default: "bg-primary text-primary-foreground [a]:hover:bg-primary/80",
				secondary: "bg-secondary text-secondary-foreground [a]:hover:bg-secondary/80",
				destructive: "bg-destructive/10 text-destructive focus-visible:ring-destructive/20 [a]:hover:bg-destructive/20",
				outline: "border-border text-foreground [a]:hover:bg-muted [a]:hover:text-muted-foreground",
				ghost: "hover:bg-muted hover:text-muted-foreground",
				link: "text-primary underline-offset-4 hover:underline",
				success: "bg-success-muted/30 border-success/20 text-success-text",
				warning: "bg-warning-muted/30 border-warning/20 text-warning-text",
				error: "bg-error-muted/30 border-error/20 text-error-text",
				info: "bg-info-muted/30 border-info/20 text-info-text",
			},
		},
		defaultVariants: {
			variant: "default",
		},
	});

	export type BadgeVariant = VariantProps<typeof badgeVariants>["variant"];
</script>

<script lang="ts">
	import { cn, type WithElementRef } from "../../../lib/utils";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		variant = "default",
		children,
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLSpanElement>, HTMLSpanElement> & {
		variant?: BadgeVariant;
	} = $props();
</script>

<span
	bind:this={ref}
	data-slot="badge"
	class={cn(badgeVariants({ variant }), className)}
	{...restProps}
>
	{@render children?.()}
</span>
