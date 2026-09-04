<script lang="ts">
  import { Eye, EyeOff } from "@lucide/svelte";
  import type { Component } from "svelte";
  import { InputGroup } from "@prismedia/ui-svelte";
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
    autocomplete?: AutoFill;
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
    autocomplete = "current-password",
  }: Props = $props();

  const id = $props.id();
  let revealed = $state(false);
</script>

<FormField {label} {icon} {helper} {error} {required} htmlFor={id}>
  <InputGroup.Root>
    <InputGroup.Input {id} type={revealed ? "text" : "password"} {disabled} {placeholder}
      {autocomplete} {value} {required} aria-invalid={!!error}
      aria-describedby={error || helper ? `${id}-message` : undefined}
      oninput={(event) => onChange(event.currentTarget.value)} />
    <InputGroup.Addon align="inline-end">
      <InputGroup.Button size="icon-xs" {disabled} aria-label={revealed ? "Hide password" : "Show password"}
        aria-pressed={revealed} onclick={() => revealed = !revealed}>
        {#if revealed}<EyeOff />{:else}<Eye />{/if}
      </InputGroup.Button>
    </InputGroup.Addon>
  </InputGroup.Root>
</FormField>
