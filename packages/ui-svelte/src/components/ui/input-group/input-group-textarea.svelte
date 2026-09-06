<script lang="ts">
	import { Textarea } from "../textarea";
	import { cn } from "../../../lib/utils";
	import type { ComponentProps } from "svelte";

	let {
		ref = $bindable(null),
		value = $bindable(),
		class: className,
		...props
	}: ComponentProps<typeof Textarea> = $props();
</script>

<Textarea
	bind:ref
	data-slot="input-group-control"
	class={cn("rounded-none border-0 bg-transparent py-2 shadow-none ring-0 focus-visible:ring-0 disabled:bg-transparent aria-invalid:ring-0   flex-1 resize-none", className)}
	bind:value
	{...props}
/>
