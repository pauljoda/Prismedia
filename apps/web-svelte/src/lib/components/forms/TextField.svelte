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
    type?: "text" | "email" | "url" | "search" | "number";
    autocomplete?: AutoFill;
    inputClass?: string;
    min?: number | string;
    max?: number | string;
    step?: number | string;
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
    type = "text",
    autocomplete = undefined,
    inputClass = "",
    min,
    max,
    step,
  }: Props = $props();

  const id = `text-${Math.random().toString(36).slice(2, 9)}`;
</script>

<FormField {label} {icon} {helper} {error} {required} htmlFor={id}>
  <input
    {id}
    {type}
    {disabled}
    {placeholder}
    {autocomplete}
    {min}
    {max}
    {step}
    {value}
    oninput={(e) => onChange((e.currentTarget as HTMLInputElement).value)}
    aria-invalid={error ? "true" : undefined}
    class={cn(
      "w-full rounded-xs border border-border-subtle bg-surface-2 px-3 py-2 text-sm text-text-primary shadow-[inset_0_2px_8px_rgba(0,0,0,0.30)] transition-colors",
      "placeholder:text-text-disabled",
      "focus:border-border-accent focus:outline-none focus:shadow-[inset_0_2px_8px_rgba(0,0,0,0.30),0_0_0_1px_rgba(242,194,106,0.35),0_0_8px_rgba(242,194,106,0.15)]",
      "disabled:cursor-not-allowed disabled:opacity-50",
      error && "border-error/60",
      inputClass,
    )}
  />
</FormField>
