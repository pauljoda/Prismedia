<script lang="ts">
	import { Command as CommandPrimitive } from "bits-ui";
	import CheckIcon from '@lucide/svelte/icons/check';
	import { cn } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		class: className,
		children,
		showIndicator = true,
		...restProps
	}: CommandPrimitive.ItemProps & { showIndicator?: boolean } = $props();
</script>

<CommandPrimitive.Item
	bind:ref
	data-slot="command-item"
	class={cn(
		"group/command-item relative flex min-h-9 cursor-default items-center gap-2 rounded-sm px-3 py-2 text-sm text-muted-foreground outline-none select-none data-selected:bg-accent data-selected:text-foreground data-disabled:pointer-events-none data-disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg]:size-4",
		className
	)}
	{...restProps}
>
	{@render children?.()}
	{#if showIndicator}
		<CheckIcon class="cn-command-item-indicator ml-auto opacity-0 group-has-[[data-slot=command-shortcut]]/command-item:hidden group-data-[checked=true]/command-item:opacity-100" />
	{/if}
</CommandPrimitive.Item>
