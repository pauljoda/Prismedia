<script module lang="ts">
  import { cva, type VariantProps } from "class-variance-authority";

  /** Compact switch sizes retained for existing Prismedia consumers. */
  export const toggleVariants = cva("", {
    variants: {
      size: { sm: "h-4 w-7", md: "h-5 w-9" },
      state: { off: "", on: "" },
      disabled: { true: "", false: "" },
    },
    defaultVariants: { size: "md", state: "off", disabled: false },
  });
  export type ToggleSize = NonNullable<VariantProps<typeof toggleVariants>["size"]>;
</script>

<script lang="ts">
  import { Switch } from "../components/ui/switch";
  import { cn } from "../lib/utils";

  interface Props {
    checked?: boolean;
    disabled?: boolean;
    size?: ToggleSize;
    id?: string;
    ariaLabel?: string;
    ariaDescribedby?: string;
    class?: string;
    onchange?: (checked: boolean) => void;
  }

  let {
    checked = false,
    disabled = false,
    size = "md",
    id,
    ariaLabel = "Toggle setting",
    ariaDescribedby,
    class: className,
    onchange,
  }: Props = $props();
</script>

<Switch
  {id}
  {disabled}
  bind:checked={() => checked, (next) => onchange?.(next)}
  size={size === "sm" ? "sm" : "default"}
  aria-label={ariaLabel}
  aria-describedby={ariaDescribedby}
  class={cn(toggleVariants({ size, state: checked ? "on" : "off", disabled }), className)}
/>
