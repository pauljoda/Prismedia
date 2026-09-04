<script lang="ts" module>
	import { type VariantProps, cva } from "class-variance-authority";
	import { cn, type WithElementRef } from "../../../lib/utils";
	import type { HTMLButtonAttributes } from "svelte/elements";

	export const buttonVariants = cva("rounded-sm border border-transparent bg-clip-padding font-body text-control font-medium focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/30 active:not-aria-[haspopup]:translate-y-px aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 [&_svg:not([class*='size-'])]:size-icon group/button inline-flex shrink-0 items-center justify-center whitespace-nowrap transition-all outline-none select-none disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0", {
		variants: {
			variant: {
				default: "bg-primary text-primary-foreground hover:bg-primary/90",
				outline: "border-border bg-card shadow-xs hover:bg-muted hover:text-foreground aria-expanded:bg-muted aria-expanded:text-foreground",
				secondary: "bg-secondary text-secondary-foreground hover:bg-secondary/80 aria-expanded:bg-secondary aria-expanded:text-secondary-foreground",
				ghost: "hover:bg-muted hover:text-foreground aria-expanded:bg-muted aria-expanded:text-foreground",
				destructive: "bg-destructive/10 text-destructive hover:bg-destructive/20 focus-visible:border-destructive/40 focus-visible:ring-destructive/20",
				link: "text-primary underline-offset-4 hover:underline",
			},
			size: {
				default: "h-control gap-control-gap px-control-pad has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2",
				xs: "h-control-xs gap-control-gap-sm rounded-xs px-2 text-caption in-data-[slot=button-group]:rounded-sm has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg:not([class*='size-'])]:size-icon-sm",
				sm: "h-control-sm gap-control-gap-sm rounded-sm px-control-pad text-label in-data-[slot=button-group]:rounded-sm has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg:not([class*='size-'])]:size-icon-sm",
				lg: "h-control-lg gap-control-gap px-control-pad-lg has-data-[icon=inline-end]:pr-3 has-data-[icon=inline-start]:pl-3",
				icon: "size-control",
				"icon-xs": "size-control-xs rounded-xs in-data-[slot=button-group]:rounded-sm [&_svg:not([class*='size-'])]:size-icon-sm",
				"icon-sm": "size-control-sm rounded-sm in-data-[slot=button-group]:rounded-sm",
				"icon-lg": "size-control-lg",
			},
		},
		defaultVariants: {
			variant: "default",
			size: "default",
		},
	});

	export type ButtonVariant = VariantProps<typeof buttonVariants>["variant"];
	export type ButtonSize = VariantProps<typeof buttonVariants>["size"];

	export type ButtonProps = WithElementRef<HTMLButtonAttributes, HTMLButtonElement> & {
			variant?: ButtonVariant;
			size?: ButtonSize;
		};
</script>

<script lang="ts">
	let {
		class: className,
		variant = "default",
		size = "default",
		ref = $bindable(null),
		type = "button",
		disabled,
		children,
		...restProps
	}: ButtonProps = $props();
</script>

	<button
		bind:this={ref}
		data-slot="button"
		class={cn(buttonVariants({ variant, size }), className)}
		{type}
		{disabled}
		{...restProps}
	>
		{@render children?.()}
	</button>
