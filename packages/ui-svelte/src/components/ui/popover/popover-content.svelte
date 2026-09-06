<script lang="ts">
	import { Popover as PopoverPrimitive } from "bits-ui";
	import { cn, type WithoutChildrenOrChild } from "../../../lib/utils";
	import PopoverPortal from "./popover-portal.svelte";
	import type { ComponentProps } from "svelte";

	const uid = $props.id();

	let {
		id = uid,
		children,
		child: contentChild,
		ref = $bindable(null),
		class: className,
		sideOffset = 6,
		collisionPadding = 8,
		align = "center",
		portalProps,
		...restProps
	}: PopoverPrimitive.ContentProps & {
		portalProps?: WithoutChildrenOrChild<ComponentProps<typeof PopoverPortal>>;
	} = $props();
</script>

<PopoverPortal {...portalProps}>
	<PopoverPrimitive.Content
		bind:ref
		{id}
		data-slot="popover-content"
		role="dialog"
		{sideOffset}
		{collisionPadding}
		{align}
		class={cn(
			"z-50 flex w-80 max-w-[calc(100vw-1rem)] max-h-[var(--bits-popover-content-available-height)] flex-col gap-3 overflow-y-auto rounded-md border border-input bg-popover p-4 text-sm text-popover-foreground shadow-elevated outline-none",
			className
		)}
		{...restProps}
	>
		{#snippet child({ props, wrapperProps, ...state })}
			{#if contentChild}
				{@render contentChild({ props: { ...props, id }, wrapperProps, ...state })}
			{:else}
				<div {...wrapperProps}>
					<!-- Bits 2.19 drops the content ID in its floating layer. Keep ARIA and typeahead linked. -->
					<div {...props} {id}>{@render children?.()}</div>
				</div>
			{/if}
		{/snippet}
	</PopoverPrimitive.Content>
</PopoverPortal>
