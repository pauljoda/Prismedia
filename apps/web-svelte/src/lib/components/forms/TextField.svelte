<script lang="ts">
  import type { Component } from "svelte";
  import { TextInput } from "@prismedia/ui-svelte";
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

  const id = $props.id();
</script>

<FormField {label} {icon} {helper} {error} {required} htmlFor={id}>
  <TextInput
    {id}
    {type}
    {disabled}
    {required}
    {placeholder}
    {autocomplete}
    {min}
    {max}
    {step}
    {value}
    oninput={(e) => onChange((e.currentTarget as HTMLInputElement).value)}
    aria-invalid={error ? "true" : undefined}
    aria-describedby={error || helper ? `${id}-message` : undefined}
    class={inputClass}
  />
</FormField>
