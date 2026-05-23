<script lang="ts">
  import type { Component } from "svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    value: boolean;
    onChange: (value: boolean) => void;
    onLabel: string;
    offLabel?: string;
    icon?: Component;
    variant?: "accent" | "warning";
    disabled?: boolean;
  }

  let {
    value,
    onChange,
    onLabel,
    offLabel,
    icon: Icon,
    variant = "accent",
    disabled = false,
  }: Props = $props();

  const onClasses = $derived(
    variant === "warning"
      ? "border border-error/40 bg-error-muted/60 text-error-text shadow-[0_0_12px_rgba(220,80,80,0.18)]"
      : "border border-border-accent bg-accent-950 text-accent-300 shadow-[0_0_12px_rgba(242,194,106,0.22)]",
  );
</script>

<button
  type="button"
  {disabled}
  aria-pressed={value}
  onclick={() => onChange(!value)}
  class={cn(
    "inline-flex items-center gap-2 px-3 py-2 text-[0.78rem] rounded-xs transition-all duration-fast",
    "disabled:opacity-50 disabled:cursor-not-allowed",
    value
      ? onClasses
      : "border border-border-subtle bg-surface-2 text-text-muted hover:border-border-accent hover:text-text-primary",
  )}
>
  {#if Icon}<Icon class="h-3.5 w-3.5" />{/if}
  {value ? onLabel : (offLabel ?? onLabel)}
</button>
