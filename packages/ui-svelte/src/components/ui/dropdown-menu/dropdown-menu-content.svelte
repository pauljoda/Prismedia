<script lang="ts">
	import { DropdownMenu as DropdownMenuPrimitive } from "bits-ui";
	import { cn, type WithoutChildrenOrChild } from "../../../lib/utils";
	import DropdownMenuPortal from "./dropdown-menu-portal.svelte";
	import type { ComponentProps } from "svelte";

	const uid = $props.id();

	let {
		id = uid,
		children,
		child: contentChild,
		ref = $bindable(null),
		sideOffset = 6,
		collisionPadding = 8,
		align = "start",
		portalProps,
		class: className,
		...restProps
	}: DropdownMenuPrimitive.ContentProps & {
		portalProps?: WithoutChildrenOrChild<ComponentProps<typeof DropdownMenuPortal>>;
	} = $props();
</script>

<DropdownMenuPortal {...portalProps}>
	<DropdownMenuPrimitive.Content
		bind:ref
		{id}
		data-slot="dropdown-menu-content"
		{sideOffset}
		{collisionPadding}
		{align}
		class={cn(
			"z-50 min-w-40 max-w-[calc(100vw-1rem)] max-h-[var(--bits-dropdown-menu-content-available-height)] overflow-x-hidden overflow-y-auto rounded-md border border-input bg-popover p-1 text-popover-foreground shadow-elevated outline-none",
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
	</DropdownMenuPrimitive.Content>
</DropdownMenuPortal>
