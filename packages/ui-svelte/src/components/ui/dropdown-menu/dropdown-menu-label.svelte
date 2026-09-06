<script lang="ts">
	import { cn, type WithElementRef } from "../../../lib/utils";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		inset,
		children,
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLDivElement>> & {
		inset?: boolean;
	} = $props();
</script>

<div
	bind:this={ref}
	data-slot="dropdown-menu-label"
	data-inset={inset}
	class={cn("px-2.5 py-2 text-sm font-medium text-foreground data-inset:pl-8", className)}
	{...restProps}
>
	{@render children?.()}
</div>
