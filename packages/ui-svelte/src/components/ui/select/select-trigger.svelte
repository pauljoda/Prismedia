<script lang="ts">
	import { Select as SelectPrimitive } from "bits-ui";
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import { cn, type WithoutChild } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		class: className,
		children,
		size = "default",
		...restProps
	}: WithoutChild<SelectPrimitive.TriggerProps> & {
		size?: "sm" | "default";
	} = $props();
</script>

<SelectPrimitive.Trigger
	bind:ref
	data-slot="select-trigger"
	data-size={size}
	class={cn(
		"flex h-control w-full cursor-pointer items-center justify-between gap-2 rounded-sm border border-input bg-surface-1 px-3 text-sm text-foreground shadow-xs outline-none transition-colors duration-fast hover:bg-surface-2 focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/30 aria-invalid:border-destructive disabled:cursor-not-allowed disabled:opacity-50 motion-reduce:transition-none [&_svg]:pointer-events-none [&_svg]:shrink-0",
		className
	)}
	{...restProps}
>
	{@render children?.()}
	<ChevronDownIcon class="size-4 text-muted-foreground pointer-events-none" />
</SelectPrimitive.Trigger>
