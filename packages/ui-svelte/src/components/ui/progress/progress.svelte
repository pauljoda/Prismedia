<script lang="ts">
	import { Progress as ProgressPrimitive } from "bits-ui";
	import { cn, type WithoutChildrenOrChild } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		class: className,
		max = 100,
		value,
		...restProps
	}: WithoutChildrenOrChild<ProgressPrimitive.RootProps> = $props();
	const limit = $derived(typeof max === "number" && Number.isFinite(max) && max > 0 ? max : 100);
	const current = $derived(value == null ? null : Math.min(limit, Math.max(0, Number.isFinite(value) ? value : 0)));
</script>

<ProgressPrimitive.Root
	bind:ref
	data-slot="progress"
	class={cn("h-1 rounded-xs bg-muted relative flex w-full items-center overflow-x-hidden", className)}
	value={current}
	max={limit}
	{...restProps}
>
	<div
		data-slot="progress-indicator"
		class="bg-primary size-full flex-1 transition-all"
		style="background: var(--progress-fill, var(--color-primary)); transform: translateX(-{100 - (100 * (current ?? 0)) / limit}%)"
	></div>
</ProgressPrimitive.Root>
