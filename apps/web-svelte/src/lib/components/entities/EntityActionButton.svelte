<script lang="ts">
  import type { Component } from "svelte";
  import { Button, buttonVariants, cn } from "@prismedia/ui-svelte";

  type Variant = "default" | "primary" | "danger";

  interface Props {
    label: string;
    icon?: Component<Record<string, unknown>>;
    href?: string;
    variant?: Variant;
    active?: boolean;
    muted?: boolean;
    disabled?: boolean;
    ariaDisabled?: boolean;
    ariaLabel?: string;
    title?: string;
    iconClass?: string;
    iconFill?: string;
    class?: string;
    onClick?: () => void | Promise<void>;
  }

  let {
    label,
    icon,
    href,
    variant = "default",
    active = false,
    muted = false,
    disabled = false,
    ariaDisabled = false,
    ariaLabel,
    title,
    iconClass = "h-3.5 w-3.5",
    iconFill,
    class: className = "",
    onClick,
  }: Props = $props();

  const classes = $derived(cn(
    buttonVariants({ variant: variant === "primary" ? "primary" : variant === "danger" ? "danger" : "outline", size: "sm" }),
    active && "bg-accent text-foreground",
    muted && "text-muted-foreground",
    className,
  ));

  function click() {
    if (disabled || ariaDisabled) return;
    void onClick?.();
  }
</script>

{#if href && !disabled && !ariaDisabled}
  <a
    class={classes}
    {href}
    aria-label={ariaLabel ?? label}
    title={title ?? ariaLabel ?? label}
  >
    {#if icon}
      {@const Icon = icon}
      <Icon class={iconClass} fill={iconFill} />
    {/if}
    <span class="entity-action-button-label">{label}</span>
  </a>
{:else}
  <Button variant={variant === "primary" ? "primary" : variant === "danger" ? "danger" : "outline"} size="sm"
    type="button"
    class={classes}
    disabled={disabled}
    aria-disabled={ariaDisabled ? "true" : undefined}
    aria-label={ariaLabel ?? label}
    title={title ?? ariaLabel ?? label}
    onclick={click}
  >
    {#if icon}
      {@const Icon = icon}
      <Icon class={iconClass} fill={iconFill} />
    {/if}
    <span class="entity-action-button-label">{label}</span>
  </Button>
{/if}
