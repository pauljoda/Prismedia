<script lang="ts">
	import { Switch as SwitchPrimitive } from "bits-ui";
	import { cn, type WithoutChildrenOrChild } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		class: className,
		checked = $bindable(false),
		size = "default",
		...restProps
	}: WithoutChildrenOrChild<SwitchPrimitive.RootProps> & {
		size?: "sm" | "default";
	} = $props();
</script>

<SwitchPrimitive.Root
	bind:ref
	bind:checked
	data-slot="switch"
	data-size={size}
	class={cn(
		"peer group/switch relative inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full border border-input px-0.5 outline-none transition-colors duration-fast focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/30 aria-invalid:border-destructive data-[state=checked]:border-primary data-[state=checked]:bg-primary data-[state=unchecked]:bg-surface-3 after:absolute after:-inset-x-2 after:-inset-y-3 data-disabled:cursor-not-allowed data-disabled:opacity-50 motion-reduce:transition-none",
		className
	)}
	{...restProps}
>
	<SwitchPrimitive.Thumb
		data-slot="switch-thumb"
		class="pointer-events-none block size-3.5 rounded-full bg-muted-foreground transition-transform duration-fast data-[state=checked]:translate-x-4 data-[state=checked]:bg-primary-foreground data-[state=unchecked]:translate-x-0 group-data-[size=sm]/switch:size-2.5 group-data-[size=sm]/switch:data-[state=checked]:translate-x-3 motion-reduce:transition-none"
	/>
</SwitchPrimitive.Root>
