<script lang="ts">
  import type { Component } from "svelte";
  import { ToggleButton } from "@prismedia/ui-svelte";

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

</script>

<ToggleButton
  variant="outline"
  bind:pressed={() => value, next => onChange(next)}
  {disabled}
  class={variant === "warning" ? "data-[state=on]:text-destructive" : undefined}
>
  {#if Icon}<Icon class="h-3.5 w-3.5" />{/if}
  {value ? onLabel : (offLabel ?? onLabel)}
</ToggleButton>
