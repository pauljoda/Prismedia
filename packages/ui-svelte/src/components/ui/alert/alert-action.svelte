<script lang="ts">
	import { cn, type WithElementRef } from "../../../lib/utils";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		children,
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLDivElement>> = $props();
</script>

<div
	bind:this={ref}
	data-slot="alert-action"
	class={cn("col-start-[-2] row-span-full row-start-1 flex items-center self-center", className)}
	{...restProps}
>
	{@render children?.()}
</div>
