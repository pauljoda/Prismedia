<script lang="ts">
  import type { Component } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
  import FormField from "./FormField.svelte";

  interface Props {
    value: string;
    onChange: (value: string) => void;
    label?: string;
    icon?: Component;
    placeholder?: string;
    helper?: string;
    error?: string;
    required?: boolean;
    disabled?: boolean;
    rows?: number;
    minHeightRem?: number;
  }

  let {
    value,
    onChange,
    label,
    icon,
    placeholder,
    helper,
    error,
    required = false,
    disabled = false,
    rows = 4,
    minHeightRem = 5,
  }: Props = $props();

  const id = `area-${Math.random().toString(36).slice(2, 9)}`;
</script>

<FormField {label} {icon} {helper} {error} {required} htmlFor={id}>
  <textarea
    {id}
    {rows}
    {disabled}
    {placeholder}
    {value}
    oninput={(e) => onChange((e.currentTarget as HTMLTextAreaElement).value)}
    aria-invalid={error ? "true" : undefined}
    style:min-height={`${minHeightRem}rem`}
    class={cn(
      "w-full resize-y border border-border-subtle bg-surface-2 px-3 py-2 text-sm leading-relaxed text-text-primary transition-colors",
      "placeholder:text-text-disabled",
      "focus:border-border-accent focus:outline-none focus:shadow-[var(--shadow-focus-accent)]",
      "disabled:cursor-not-allowed disabled:opacity-50",
      error && "border-error/60",
    )}
  ></textarea>
</FormField>
