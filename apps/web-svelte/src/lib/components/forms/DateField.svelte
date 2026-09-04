<script lang="ts">
  import type { Component } from "svelte";
  import { TextInput } from "@prismedia/ui-svelte";
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

  const id = $props.id();
</script>

<FormField {label} {icon} {helper} {error} {required} htmlFor={id}>
  <TextInput
    {id}
    type="date"
    {disabled}
    {min}
    {max}
    {value}
    oninput={(e) => onChange((e.currentTarget as HTMLInputElement).value)}
    aria-invalid={error ? "true" : undefined}
    {required}
    aria-describedby={error || helper ? `${id}-message` : undefined}
    class="[color-scheme:dark]"
  />
</FormField>
