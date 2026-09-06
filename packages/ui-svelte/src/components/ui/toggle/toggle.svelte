<script lang="ts" module>
	import { type VariantProps, cva } from "class-variance-authority";

	export const toggleVariants = cva(
		"group/toggle inline-flex items-center justify-center gap-control-gap rounded-sm text-control font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground data-[state=on]:bg-accent data-[state=on]:text-foreground data-[state=on]:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40 disabled:pointer-events-none disabled:opacity-50 [&_svg]:size-icon [&_svg]:pointer-events-none [&_svg]:shrink-0", {
		variants: {
			variant: {
				default: "bg-transparent",
				outline: "border border-input bg-card hover:bg-accent",
			},
			size: {
				default: "h-control min-w-control px-control-pad has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2",
				sm: "h-control-sm min-w-control-sm rounded-xs px-control-pad text-label has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg:not([class*='size-'])]:size-icon-sm",
				lg: "h-control-lg min-w-control-lg px-control-pad-lg has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2",
			},
		},
		defaultVariants: {
			variant: "default",
			size: "default",
		},
	});

	export type ToggleVariant = VariantProps<typeof toggleVariants>["variant"];
	export type ToggleSize = VariantProps<typeof toggleVariants>["size"];
	export type ToggleVariants = VariantProps<typeof toggleVariants>;
</script>

<script lang="ts">
	import { Toggle as TogglePrimitive } from "bits-ui";
	import { cn } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		pressed = $bindable(false),
		class: className,
		size = "default",
		variant = "default",
		...restProps
	}: TogglePrimitive.RootProps & {
		variant?: ToggleVariant;
		size?: ToggleSize;
	} = $props();
</script>

<TogglePrimitive.Root
	bind:ref
	bind:pressed
	data-slot="toggle"
	class={cn(toggleVariants({ variant, size }), className)}
	{...restProps}
/>
