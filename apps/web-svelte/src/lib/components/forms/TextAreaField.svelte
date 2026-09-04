<script lang="ts">
  import type { Component } from "svelte";
  import { Textarea } from "@prismedia/ui-svelte";
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

  const id = $props.id();
</script>

<FormField {label} {icon} {helper} {error} {required} htmlFor={id}>
  <Textarea
    {id}
    {rows}
    {disabled}
    {required}
    {placeholder}
    {value}
    oninput={(e) => onChange((e.currentTarget as HTMLTextAreaElement).value)}
    aria-invalid={error ? "true" : undefined}
    aria-describedby={error || helper ? `${id}-message` : undefined}
    style={`min-height: ${minHeightRem}rem`}
    class="resize-y"
  ></Textarea>
</FormField>
