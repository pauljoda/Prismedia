<script module lang="ts">
  import { cva, type VariantProps } from "class-variance-authority";

  /** Prismedia sizing and validation variants over the shadcn-svelte Select base. */
  export const selectTriggerVariants = cva("w-full", {
    variants: {
      size: { sm: "h-8 px-2.5 text-xs", md: "h-9 px-3 text-sm", lg: "h-10 px-3.5 text-sm" },
      variant: { default: "", error: "border-destructive" },
    },
    defaultVariants: { size: "md", variant: "default" },
  });

  export type SelectSize = NonNullable<VariantProps<typeof selectTriggerVariants>["size"]>;
  export type SelectVariant = NonNullable<VariantProps<typeof selectTriggerVariants>["variant"]>;

  /** A selectable value; annotations convey status without preventing selection. */
  export interface SelectOption {
    value: string;
    label: string;
    annotation?: string;
    disabled?: boolean;
  }
</script>

<script lang="ts">
  import { cn } from "../lib/utils";
  import * as SelectBase from "../components/ui/select";
  import Badge from "./Badge.svelte";

  interface Props {
    options: SelectOption[];
    value?: string;
    placeholder?: string;
    disabled?: boolean;
    size?: SelectSize;
    variant?: SelectVariant;
    class?: string;
    ariaLabel?: string;
    onchange?: (value: string) => void;
  }

  let {
    options,
    value = $bindable(),
    placeholder = "Select...",
    disabled = false,
    size = "md",
    variant = "default",
    class: className,
    ariaLabel,
    onchange,
  }: Props = $props();

  let trigger = $state<HTMLButtonElement | null>(null);
  const selectedOption = $derived(options.find((option) => option.value === value));

  function select(next: string) {
    if (next === value) return;
    value = next;
    onchange?.(next);
  }
</script>

<SelectBase.Root type="single" value={value ?? ""} items={options} onValueChange={select} {disabled} allowDeselect={false}>
  <SelectBase.Trigger
    bind:ref={trigger}
    aria-label={ariaLabel}
    aria-invalid={variant === "error" || undefined}
    class={cn(selectTriggerVariants({ size, variant }), className)}
  >
    <span class={cn("min-w-0 truncate", !selectedOption && "text-muted-foreground")}>
      {selectedOption?.label ?? placeholder}
    </span>
  </SelectBase.Trigger>
  <SelectBase.Content
    align="start"
    collisionPadding={8}
    portalProps={{ to: trigger?.closest("dialog") ?? undefined }}
  >
    <SelectBase.Group aria-label={ariaLabel}>
      {#each options as option (option.value)}
        <SelectBase.Item value={option.value} label={option.label} disabled={option.disabled}>
          <span class="min-w-0 flex-1 [overflow-wrap:anywhere]">{option.label}</span>
          {#if option.annotation}
            <Badge>{option.annotation}</Badge>
          {/if}
        </SelectBase.Item>
      {/each}
    </SelectBase.Group>
  </SelectBase.Content>
</SelectBase.Root>
