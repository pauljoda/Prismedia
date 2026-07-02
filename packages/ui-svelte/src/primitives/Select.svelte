<script module lang="ts">
  import { cva, type VariantProps } from "class-variance-authority";

  export const selectTriggerVariants = cva(
    [
      "w-full bg-surface-1 text-text-primary border rounded-xs",
      "shadow-[inset_0_1px_3px_rgba(0,0,0,0.25)]",
      "transition-all duration-fast cursor-pointer",
      "flex items-center justify-between gap-2",
      "focus:outline-none focus:border-border-accent-strong focus:shadow-[var(--shadow-focus-accent)]",
      "disabled:opacity-40 disabled:cursor-not-allowed",
    ].join(" "),
    {
      variants: {
        size: {
          sm: "h-8 px-2.5 text-xs",
          md: "h-9 px-3 text-sm",
          lg: "h-10 px-3.5 text-sm",
        },
        variant: {
          default: "border-border-default",
          error: "border-error/40",
        },
      },
      defaultVariants: { size: "md", variant: "default" },
    },
  );

  export type SelectSize = NonNullable<VariantProps<typeof selectTriggerVariants>["size"]>;
  export type SelectVariant = NonNullable<VariantProps<typeof selectTriggerVariants>["variant"]>;

  export interface SelectOption {
    value: string;
    label: string;
    disabled?: boolean;
  }
</script>

<script lang="ts">
  import { cn } from "../lib/utils";
  import { ChevronDown } from "@lucide/svelte";
  import { tick } from "svelte";

  interface Props {
    options: SelectOption[];
    value?: string;
    placeholder?: string;
    disabled?: boolean;
    size?: SelectSize;
    variant?: SelectVariant;
    class?: string;
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
    onchange,
  }: Props = $props();

  let open = $state(false);
  let focusedIndex = $state(-1);
  let triggerEl: HTMLButtonElement | undefined = $state();
  let listEl: HTMLDivElement | undefined = $state();
  let dropUp = $state(false);
  let maxHeight = $state(240);

  const selectedOption = $derived(options.find((o) => o.value === value));
  const enabledOptions = $derived(options.filter((o) => !o.disabled));

  function toggle() {
    if (disabled) return;
    if (open) {
      close();
    } else {
      openMenu();
    }
  }

  async function openMenu() {
    open = true;
    focusedIndex = value ? options.findIndex((o) => o.value === value) : 0;
    if (focusedIndex < 0) focusedIndex = 0;

    await tick();
    positionDropdown();
    // preventScroll: the menu is absolutely positioned, so letting the browser scroll it "into view"
    // on focus shifts the whole page under the dropdown instead of just overlaying it.
    listEl?.focus({ preventScroll: true });
  }

  function close() {
    open = false;
    focusedIndex = -1;
    triggerEl?.focus({ preventScroll: true });
  }

  function select(option: SelectOption) {
    if (option.disabled) return;
    value = option.value;
    onchange?.(option.value);
    close();
  }

  function positionDropdown() {
    if (!triggerEl) return;
    const rect = triggerEl.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom - 8;
    const spaceAbove = rect.top - 8;
    const menuHeight = Math.min(options.length * 36 + 8, 240);

    if (spaceBelow >= menuHeight || spaceBelow >= spaceAbove) {
      dropUp = false;
      maxHeight = Math.max(Math.min(spaceBelow, 240), 120);
    } else {
      dropUp = true;
      maxHeight = Math.max(Math.min(spaceAbove, 240), 120);
    }
  }

  function onKeydown(e: KeyboardEvent) {
    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        focusedIndex = Math.min(focusedIndex + 1, options.length - 1);
        scrollFocusedIntoView();
        break;
      case "ArrowUp":
        e.preventDefault();
        focusedIndex = Math.max(focusedIndex - 1, 0);
        scrollFocusedIntoView();
        break;
      case "Enter":
      case " ":
        e.preventDefault();
        if (focusedIndex >= 0 && focusedIndex < options.length) {
          select(options[focusedIndex]);
        }
        break;
      case "Escape":
        e.preventDefault();
        close();
        break;
      case "Home":
        e.preventDefault();
        focusedIndex = 0;
        scrollFocusedIntoView();
        break;
      case "End":
        e.preventDefault();
        focusedIndex = options.length - 1;
        scrollFocusedIntoView();
        break;
    }
  }

  function scrollFocusedIntoView() {
    tick().then(() => {
      listEl?.querySelector("[data-focused]")?.scrollIntoView({ block: "nearest" });
    });
  }

  function onClickOutside(e: MouseEvent) {
    if (!triggerEl?.contains(e.target as Node) && !listEl?.contains(e.target as Node)) {
      close();
    }
  }
</script>

<svelte:window onclick={open ? onClickOutside : undefined} />

<div class="relative" class:z-50={open}>
  <button
    bind:this={triggerEl}
    type="button"
    {disabled}
    aria-haspopup="listbox"
    aria-expanded={open}
    onclick={toggle}
    onkeydown={(e) => {
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        if (!open) openMenu();
      }
    }}
    class={cn(selectTriggerVariants({ size, variant }), className)}
  >
    <span class={cn(!selectedOption && "text-text-disabled")}>
      {selectedOption?.label ?? placeholder}
    </span>
    <ChevronDown
      class={cn(
        "h-3.5 w-3.5 shrink-0 text-text-muted transition-transform duration-fast",
        open && "rotate-180",
      )}
    />
  </button>

  {#if open}
    <div
      bind:this={listEl}
      role="listbox"
      tabindex="-1"
      aria-activedescendant={focusedIndex >= 0 ? `opt-${focusedIndex}` : undefined}
      onkeydown={onKeydown}
      class={cn(
        "absolute left-0 right-0 z-50 overflow-y-auto",
        "bg-surface-3 border border-border-default rounded-sm",
        "shadow-[0_8px_40px_rgba(0,0,0,0.6),inset_0_1px_0_rgba(255,255,255,0.05)]",
        "py-1",
        dropUp ? "bottom-full mb-1" : "top-full mt-1",
      )}
      style="max-height: {maxHeight}px"
    >
      {#each options as option, i (option.value)}
        <button
          id={`opt-${i}`}
          type="button"
          role="option"
          aria-selected={option.value === value}
          aria-disabled={option.disabled}
          data-focused={i === focusedIndex ? "" : undefined}
          onclick={() => select(option)}
          onmouseenter={() => (focusedIndex = i)}
          class={cn(
            "flex w-full items-center px-3 py-2 text-sm text-left transition-colors duration-fast",
            option.disabled && "opacity-40 cursor-not-allowed",
            i === focusedIndex
              ? "bg-surface-4 text-text-primary"
              : "text-text-secondary",
            option.value === value && "text-text-accent",
          )}
        >
          {option.label}
        </button>
      {/each}
    </div>
  {/if}
</div>
