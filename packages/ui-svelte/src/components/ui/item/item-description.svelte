<script lang="ts">
	import { cn, type WithElementRef } from "../../../lib/utils";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		children,
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLParagraphElement>> = $props();
</script>

<p
	bind:this={ref}
	data-slot="item-description"
	class={cn(
		"text-left text-sm leading-normal text-muted-foreground group-data-[size=xs]/item:text-xs line-clamp-2 font-normal [&>a]:underline [&>a]:underline-offset-4 [&>a:hover]:text-primary",
		className
	)}
	{...restProps}
>
	{@render children?.()}
</p>
