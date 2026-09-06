<script lang="ts">
	import { Tabs as TabsPrimitive } from "bits-ui";
	import { cn } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		class: className,
		...restProps
	}: TabsPrimitive.TriggerProps = $props();
</script>

<TabsPrimitive.Trigger
	bind:ref
	data-slot="tabs-trigger"
	class={cn(
		"relative inline-flex min-h-9 shrink-0 items-center justify-center gap-2 whitespace-nowrap rounded-sm border border-transparent px-3 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-accent/50 hover:text-foreground focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/30 focus-visible:outline-1 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg]:size-4 group-data-[orientation=vertical]/tabs:w-full group-data-[orientation=vertical]/tabs:justify-start",
		"data-[state=active]:bg-accent data-[state=active]:text-foreground group-data-[variant=default]/tabs-list:data-[state=active]:shadow-sm",
		"group-data-[variant=line]/tabs-list:min-h-12 group-data-[variant=line]/tabs-list:rounded-none group-data-[variant=line]/tabs-list:px-4 group-data-[variant=line]/tabs-list:data-[state=active]:bg-accent/50",
		"after:absolute after:bg-[var(--tabs-indicator,var(--color-primary))] after:opacity-0 after:transition-opacity group-data-[orientation=horizontal]/tabs:after:inset-x-3 group-data-[orientation=horizontal]/tabs:after:bottom-0 group-data-[orientation=horizontal]/tabs:after:h-0.5 group-data-[orientation=vertical]/tabs:after:inset-y-2 group-data-[orientation=vertical]/tabs:after:right-0 group-data-[orientation=vertical]/tabs:after:w-0.5 group-data-[variant=line]/tabs-list:data-[state=active]:after:opacity-100",
		className
	)}
	{...restProps}
/>
