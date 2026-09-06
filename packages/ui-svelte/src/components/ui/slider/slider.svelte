<script lang="ts">
	import { Slider as SliderPrimitive } from "bits-ui";
	import { cn, type WithoutChildrenOrChild } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		value = $bindable(),
		orientation = "horizontal",
		class: className,
		thumbLabel,
		...restProps
	}: WithoutChildrenOrChild<Extract<SliderPrimitive.RootProps, { type: "single" }>> & {
		/** Accessible name applied to each focusable thumb, not just the track. */
		thumbLabel?: string;
	} = $props();
</script>

<!--
This adapter exposes the single-thumb form used by Prismedia controls.
-->
<SliderPrimitive.Root
	bind:ref
	bind:value
	data-slot="slider"
	{orientation}
	class={cn(
		"data-[orientation=vertical]:min-h-40 relative flex min-h-8 w-full touch-none items-center select-none data-disabled:opacity-50 data-[orientation=vertical]:h-full data-[orientation=vertical]:w-auto data-[orientation=vertical]:flex-col",
		className
	)}
	{...restProps}
>
	{#snippet children({ thumbItems })}
		<span
			data-slot="slider-track"
			data-orientation={orientation}
			class={cn(
				"relative grow overflow-hidden rounded-xs bg-accent data-[orientation=horizontal]:h-1 data-[orientation=horizontal]:w-full data-[orientation=vertical]:h-full data-[orientation=vertical]:w-1"
			)}
		>
			<SliderPrimitive.Range
				data-slot="slider-range"
				class={cn(
					"bg-primary absolute select-none data-[orientation=horizontal]:h-full data-[orientation=vertical]:w-full"
				)}
			/>
		</span>
		{#each thumbItems as thumb (thumb.index)}
			<SliderPrimitive.Thumb
				data-slot="slider-thumb"
				index={thumb.index}
				aria-label={thumbLabel}
				class="relative block size-4 shrink-0 select-none rounded-sm border border-ring bg-primary ring-ring/40 transition-shadow after:absolute after:-inset-2 hover:ring-2 focus-visible:ring-2 focus-visible:outline-none active:ring-2 disabled:pointer-events-none disabled:opacity-50"
			/>
		{/each}
	{/snippet}
</SliderPrimitive.Root>
