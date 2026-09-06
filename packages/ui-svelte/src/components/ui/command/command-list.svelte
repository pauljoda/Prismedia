<script lang="ts">
	import { Command as CommandPrimitive } from "bits-ui";
	import { cn } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		class: className,
		children,
		...restProps
	}: CommandPrimitive.ListProps = $props();
</script>

<CommandPrimitive.List
	bind:ref
	data-slot="command-list"
	class={cn("max-h-72 scroll-py-1 outline-none overflow-x-hidden overflow-y-auto", className)}
	{...restProps}
>
	<!-- Bits 2.19 uses the viewport to connect the input's active descendant and list. -->
	<CommandPrimitive.Viewport>{@render children?.()}</CommandPrimitive.Viewport>
</CommandPrimitive.List>
