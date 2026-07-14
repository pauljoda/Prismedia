<script lang="ts">
  import type { HTMLInputAttributes } from "svelte/elements";
  import { Check } from "@lucide/svelte";
  import { cva } from "class-variance-authority";
  import { cn } from "../lib/utils";

  const rootVariants = cva("relative inline-flex shrink-0 items-center justify-center", {
    variants: {
      size: {
        sm: "h-3.5 w-3.5",
        md: "h-4 w-4",
      },
    },
    defaultVariants: { size: "sm" },
  });

  const boxVariants = cva(
    "pointer-events-none flex items-center justify-center rounded-xs border transition-all duration-fast",
    {
      variants: {
        size: { sm: "h-3.5 w-3.5", md: "h-4 w-4" },
        state: {
          unchecked: "border-border-subtle bg-surface-3",
          checked:
            "border-border-accent bg-accent-500 shadow-[0_0_6px_rgba(199, 201, 204,0.35)]",
        },
        disabled: { true: "cursor-not-allowed opacity-40", false: "" },
      },
      defaultVariants: { size: "sm", state: "unchecked", disabled: false },
    },
  );

  const iconVariants = cva(
    "text-surface-1 transition-opacity duration-fast pointer-events-none",
    {
      variants: {
        size: { sm: "h-2.5 w-2.5", md: "h-3 w-3" },
        visible: { true: "opacity-100", false: "opacity-0" },
      },
      defaultVariants: { size: "sm", visible: false },
    },
  );

  type Size = "sm" | "md";

  interface Props extends Omit<HTMLInputAttributes, "type" | "size" | "class"> {
    size?: Size;
    /** Sets the native `indeterminate` state (not a controlled HTML attribute). */
    indeterminate?: boolean;
    class?: string;
  }

  let {
    size = "sm",
    indeterminate = false,
    checked = false,
    disabled = false,
    class: className,
    ...rest
  }: Props = $props();

  let inputEl: HTMLInputElement | undefined = $state();

  $effect(() => {
    if (inputEl) {
      inputEl.indeterminate = indeterminate === true;
    }
  });

  const isChecked = $derived(Boolean(checked));
</script>

<div class={cn(rootVariants({ size }), className)}>
  <input
    bind:this={inputEl}
    type="checkbox"
    {disabled}
    checked={isChecked}
    class="absolute inset-0 z-10 m-0 h-full w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
    {...rest}
  />
  <span
    class={boxVariants({
      size,
      state: isChecked ? "checked" : "unchecked",
      disabled: Boolean(disabled),
    })}
    aria-hidden="true"
  >
    <Check class={iconVariants({ size, visible: isChecked })} strokeWidth={3} />
  </span>
</div>
