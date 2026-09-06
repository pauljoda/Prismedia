<script lang="ts">
	import { Select as SelectPrimitive } from "bits-ui";
	import { cn, type WithoutChild } from "../../../lib/utils";
	import type { WithoutChildrenOrChild } from "../../../lib/utils";
	import SelectPortal from "./select-portal.svelte";
	import SelectScrollDownButton from "./select-scroll-down-button.svelte";
	import SelectScrollUpButton from "./select-scroll-up-button.svelte";
	import type { ComponentProps } from "svelte";

	let {
		ref = $bindable(null),
		class: className,
		sideOffset = 4,
		portalProps,
		children,
		preventScroll = true,
		...restProps
	}: WithoutChild<SelectPrimitive.ContentProps> & {
		portalProps?: WithoutChildrenOrChild<ComponentProps<typeof SelectPortal>>;
	} = $props();
</script>

<SelectPortal {...portalProps}>
	<SelectPrimitive.Content
		bind:ref
		{sideOffset}
		{preventScroll}
		data-slot="select-content"
		class={cn(
			"relative isolate z-50 min-w-(--bits-select-anchor-width) max-w-[calc(100vw-1rem)] max-h-[min(15rem,var(--bits-select-content-available-height))] overflow-x-hidden overflow-y-auto rounded-md border border-input bg-popover text-popover-foreground shadow-elevated",
			className
		)}
		{...restProps}
	>
		<SelectScrollUpButton />
		<SelectPrimitive.Viewport
			class={cn(
				"w-full scroll-my-1"
			)}
		>
			{@render children?.()}
		</SelectPrimitive.Viewport>
		<SelectScrollDownButton />
	</SelectPrimitive.Content>
</SelectPortal>
