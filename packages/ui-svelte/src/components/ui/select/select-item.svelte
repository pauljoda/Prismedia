<script lang="ts">
	import { Select as SelectPrimitive } from "bits-ui";
	import CheckIcon from '@lucide/svelte/icons/check';
	import { cn, type WithoutChild } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		class: className,
		value,
		label,
		disabled = false,
		children: childrenProp,
		...restProps
	}: WithoutChild<SelectPrimitive.ItemProps> = $props();
</script>

<SelectPrimitive.Item
	bind:ref
	{value}
	{label}
	{disabled}
	aria-disabled={disabled || undefined}
	data-slot="select-item"
	class={cn(
		"relative flex min-h-9 w-full cursor-default select-none items-center gap-2 rounded-sm py-2 pr-8 pl-2.5 text-sm text-text-secondary outline-none data-highlighted:bg-accent data-highlighted:text-accent-foreground data-selected:text-foreground data-disabled:pointer-events-none data-disabled:opacity-50 [@media(pointer:coarse)]:min-h-11 [&_svg]:pointer-events-none [&_svg]:size-3.5 [&_svg]:shrink-0",
		className
	)}
	{...restProps}
>
	{#snippet children({ selected, highlighted })}
		<span class="absolute end-2 flex size-3.5 items-center justify-center">
			{#if selected}
				<CheckIcon class="cn-select-item-indicator-icon" />
			{/if}
		</span>
		<span class="flex min-w-0 flex-1 items-center gap-2">
			{#if childrenProp}
				{@render childrenProp({ selected, highlighted })}
			{:else}
				{label || value}
			{/if}
		</span>
	{/snippet}
</SelectPrimitive.Item>
