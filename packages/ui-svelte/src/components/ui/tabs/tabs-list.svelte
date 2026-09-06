<script lang="ts" module>
	import { cva, type VariantProps } from "class-variance-authority";

	export const tabsListVariants = cva("group/tabs-list inline-flex min-h-11 w-fit items-center justify-center rounded-md p-1 text-muted-foreground group-data-[orientation=vertical]/tabs:h-fit group-data-[orientation=vertical]/tabs:flex-col", {
		variants: {
			variant: {
				default: "cn-tabs-list-variant-default bg-muted",
				line: "cn-tabs-list-variant-line min-h-12 w-full justify-start gap-1 rounded-b-none border-b border-border bg-muted/60 px-2 py-0",
			},
		},
		defaultVariants: {
			variant: "default",
		},
	});

	export type TabsListVariant = VariantProps<typeof tabsListVariants>["variant"];
</script>

<script lang="ts">
	import { Tabs as TabsPrimitive } from "bits-ui";
	import { cn } from "../../../lib/utils";

	let {
		ref = $bindable(null),
		variant = "default",
		class: className,
		...restProps
	}: TabsPrimitive.ListProps & {
		variant?: TabsListVariant;
	} = $props();
</script>

<TabsPrimitive.List
	bind:ref
	data-slot="tabs-list"
	data-variant={variant}
	class={cn(tabsListVariants({ variant }), className)}
	{...restProps}
/>
