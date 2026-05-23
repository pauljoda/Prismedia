<script module lang="ts">
  import { cva, type VariantProps } from "class-variance-authority";

  export const toggleVariants = cva(
    "relative shrink-0 border transition-colors duration-fast cursor-pointer",
    {
      variants: {
        size: {
          sm: "w-7 h-4",
          md: "w-9 h-5",
        },
        state: {
          off: "border-border-default bg-surface-1",
          on: "border-border-accent bg-accent-950/30",
        },
        disabled: {
          true: "opacity-40 cursor-not-allowed",
          false: "",
        },
      },
      defaultVariants: { size: "md", state: "off", disabled: false },
    },
  );

  export type ToggleSize = NonNullable<VariantProps<typeof toggleVariants>["size"]>;
</script>

<script lang="ts">
  import { cn } from "../lib/utils";

  interface Props {
    checked?: boolean;
    disabled?: boolean;
    size?: ToggleSize;
    class?: string;
    onchange?: (checked: boolean) => void;
  }

  let {
    checked = false,
    disabled = false,
    size = "md",
    class: className,
    onchange,
  }: Props = $props();

  const knobSize = $derived(size === "sm" ? "w-2.5 h-2.5" : "w-3.5 h-3.5");
  const knobOffset = $derived(
    checked
      ? size === "sm" ? "left-[0.85rem]" : "left-[1.1rem]"
      : "left-0.5",
  );
  const ledSize = $derived(size === "sm" ? "led-sm" : "led-sm");
</script>

<button
  type="button"
  role="switch"
  aria-checked={checked}
  {disabled}
  onclick={() => { if (!disabled) onchange?.(!checked); }}
  class={cn(
    toggleVariants({ size, state: checked ? "on" : "off", disabled }),
    "rounded-full",
    className,
  )}
>
  <div
    class={cn(
      "absolute top-0.5 bottom-0.5 bg-surface-3 border border-border-subtle rounded-full",
      "transition-all duration-fast flex items-center justify-center shadow-sm",
      knobSize,
      knobOffset,
      checked && "border-border-accent",
    )}
  >
    <div class={cn("led", ledSize, checked ? "led-active" : "led-idle")}></div>
  </div>
</button>
