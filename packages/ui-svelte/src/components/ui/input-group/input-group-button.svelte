<script lang="ts" module>
	import { cva, type VariantProps } from "class-variance-authority";

	const inputGroupButtonVariants = cva("gap-2 text-sm flex items-center shadow-none", {
		variants: {
			size: {
				xs: "h-6 gap-1 rounded-xs px-1.5 [&>svg:not([class*='size-'])]:size-3.5",
				sm: "h-8 gap-1.5 px-2",
				"icon-xs": "size-6 rounded-xs p-0 has-[>svg]:p-0",
				"icon-sm": "size-8 p-0 has-[>svg]:p-0",
			},
		},
		defaultVariants: {
			size: "xs",
		},
	});

	export type InputGroupButtonSize = VariantProps<typeof inputGroupButtonVariants>["size"];
</script>

<script lang="ts">
	import { Button } from "../button";
	import { cn } from "../../../lib/utils";
	import type { ComponentProps } from "svelte";

	let {
		ref = $bindable(null),
		class: className,
		children,
		type = "button",
		variant = "ghost",
		size = "xs",
		...restProps
	}: Omit<ComponentProps<typeof Button>, "href" | "size"> & {
		size?: InputGroupButtonSize;
	} = $props();
</script>

<Button
	bind:ref
	{type}
	data-slot="input-group-button"
	data-size={size}
	{variant}
	class={cn(inputGroupButtonVariants({ size }), className)}
	{...restProps}
>
	{@render children?.()}
</Button>
