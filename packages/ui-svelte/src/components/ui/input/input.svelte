<script module lang="ts">
  export const inputStyles = "h-control rounded-sm border border-input bg-background px-2.5 py-1 text-base transition-colors file:h-6 file:text-sm file:font-medium focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/30 disabled:bg-input/50 aria-invalid:border-destructive aria-invalid:ring-2 aria-invalid:ring-destructive/20 md:text-sm w-full min-w-0 outline-none file:inline-flex file:border-0 file:bg-transparent file:text-foreground placeholder:text-muted-foreground disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50";
</script>

<script lang="ts">
	import { cn, type WithElementRef } from "../../../lib/utils";
	import type { HTMLInputAttributes } from "svelte/elements";

	type Props = WithElementRef<HTMLInputAttributes, HTMLInputElement>;

	let {
		ref = $bindable(null),
		value = $bindable(),
		type,
		files = $bindable(),
		class: className,
		"data-slot": dataSlot = "input",
		...restProps
	}: Props = $props();
</script>

{#if type === "file"}
	<input
		bind:this={ref}
		data-slot={dataSlot}
		class={cn(
			inputStyles,
			className
		)}
		type="file"
		bind:files
		bind:value
		{...restProps}
	/>
{:else}
	<input
		bind:this={ref}
		data-slot={dataSlot}
		class={cn(
			inputStyles,
			className
		)}
		{type}
		bind:value
		{...restProps}
	/>
{/if}
