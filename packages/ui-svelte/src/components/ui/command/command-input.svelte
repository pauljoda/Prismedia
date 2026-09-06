<script lang="ts">
	import { Command as CommandPrimitive } from "bits-ui";
	import { textInputVariants } from "../../../primitives/TextInput.svelte";
	import SearchIcon from '@lucide/svelte/icons/search';
	import { cn } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		class: className,
		value = $bindable(""),
		...restProps
	}: CommandPrimitive.InputProps = $props();
</script>

<div data-slot="command-input-wrapper" class="relative p-1">
	<SearchIcon class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
	<CommandPrimitive.Input
		bind:value
		bind:ref
		data-slot="command-input"
		class={cn(textInputVariants({ size: "md" }), "pl-9", className)}
		{...restProps}
	/>
</div>
