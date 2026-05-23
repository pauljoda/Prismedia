<script lang="ts">
  import type { Component, Snippet } from "svelte";
  import { cn } from "@prismedia/ui-svelte";

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

<div class={cn("space-y-1.5", className)}>
  {#if label}
    <label class="text-kicker inline-flex items-center gap-1.5" for={htmlFor}>
      {#if Icon}<Icon class="h-3 w-3" />{/if}
      {label}
      {#if required}<span class="text-error-text" aria-label="required">*</span>{/if}
    </label>
  {/if}
  {@render children()}
  {#if error}
    <p class="text-[0.7rem] text-error-text">{error}</p>
  {:else if helper}
    <p class="text-[0.7rem] text-text-disabled">{helper}</p>
  {/if}
</div>
