<script lang="ts">
  import type { Component, Snippet } from "svelte";
  import { Field } from "@prismedia/ui-svelte";

  interface Props {
    label?: string;
    icon?: Component;
    helper?: string;
    error?: string;
    required?: boolean;
    htmlFor?: string;
    class?: string;
    children: Snippet;
  }

  let {
    label,
    icon: Icon,
    helper,
    error,
    required = false,
    htmlFor,
    class: className = "",
    children,
  }: Props = $props();
</script>

<Field.Field class={className} data-invalid={Boolean(error)}>
  {#if label}
    <Field.Label for={htmlFor}>
      {#if Icon}<Icon class="size-3.5" />{/if}
      {label}
      {#if required}<span class="text-destructive" aria-hidden="true">*</span>{/if}
    </Field.Label>
  {/if}
  {@render children()}
  {#if error}
    <Field.Error id={htmlFor ? `${htmlFor}-message` : undefined}>{error}</Field.Error>
  {:else if helper}
    <Field.Description id={htmlFor ? `${htmlFor}-message` : undefined}>{helper}</Field.Description>
  {/if}
</Field.Field>
