<script lang="ts">
  import type { Component } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
  import FormField from "./FormField.svelte";

  interface Props {
    value: string;
    onChange: (value: string) => void;
    label?: string;
    icon?: Component;
    helper?: string;
    error?: string;
    required?: boolean;
    disabled?: boolean;
    min?: string;
    max?: string;
  }

  let {
    value,
    onChange,
    label,
    icon,
    helper,
    error,
    required = false,
    disabled = false,
    min,
    max,
  }: Props = $props();

  const id = `date-${Math.random().toString(36).slice(2, 9)}`;
</script>

<FormField {label} {icon} {helper} {error} {required} htmlFor={id}>
  <input
    {id}
    type="date"
    {disabled}
    {min}
    {max}
    {value}
    oninput={(e) => onChange((e.currentTarget as HTMLInputElement).value)}
    aria-invalid={error ? "true" : undefined}
    class={cn(
      "w-full border border-border-subtle bg-surface-2 px-3 py-2 text-sm text-text-primary transition-colors",
      "focus:border-border-accent focus:outline-none focus:shadow-[var(--shadow-focus-accent)]",
      "disabled:cursor-not-allowed disabled:opacity-50",
      "[color-scheme:dark]",
      error && "border-error/60",
    )}
  />
</FormField>
