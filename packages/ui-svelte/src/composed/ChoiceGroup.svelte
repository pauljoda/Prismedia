<script module lang="ts">
  import type { Component } from "svelte";

  /** A labeled choice with optional identity artwork and a quiet result count. */
  export interface ChoiceOption<T extends string = string> {
    value: T;
    label: string;
    icon?: Component;
    iconColor?: string;
    count?: number;
    disabled?: boolean;
  }
</script>

<script lang="ts" generics="T extends string">
  import * as ToggleGroup from "../components/ui/toggle-group";
  import type { ToggleSize, ToggleVariant } from "../components/ui/toggle";
  import { cn } from "../lib/utils";

  /** Controlled choices that retain at least one selection. Use ToggleGroup directly for optional toggles. */
  type Props = {
    options: ChoiceOption<T>[];
    ariaLabel: string;
    disabled?: boolean;
    size?: ToggleSize;
    variant?: ToggleVariant;
    class?: string;
  } & (
    | { type: "single"; value: T; onValueChange: (value: T) => void }
    | { type: "multiple"; value: T[]; onValueChange: (value: T[]) => void }
  );

  let props: Props = $props();
  const groupClass = $derived(cn("w-full min-w-0 flex-wrap justify-start", props.class));

  function selectOne(next: string) {
    if (props.type !== "single" || props.disabled || next === props.value) return;
    const option = props.options.find(option => option.value === next && !option.disabled);
    if (option) props.onValueChange(option.value);
  }

  function selectMany(next: string[]) {
    if (props.type !== "multiple" || props.disabled) return;
    const selected = props.options.filter(option => next.includes(option.value)).map(option => option.value);
    if (selected.length > 0) props.onValueChange(selected);
  }
</script>

{#snippet choices()}
  {#each props.options as option (option.value)}
    <ToggleGroup.Item value={option.value} disabled={option.disabled}>
      {#if option.icon}<option.icon color={option.iconColor} aria-hidden="true" />{/if}
      {option.label}
      {#if option.count !== undefined}
        <span class="font-mono text-xs tabular-nums text-muted-foreground">{option.count}</span>
      {/if}
    </ToggleGroup.Item>
  {/each}
{/snippet}

{#if props.type === "multiple"}
  <ToggleGroup.Root type="multiple" bind:value={() => props.type === "multiple" ? props.value : [], selectMany}
    variant={props.variant ?? "default"} spacing={2} size={props.size ?? "default"} disabled={props.disabled}
    aria-label={props.ariaLabel} class={groupClass}>
    {@render choices()}
  </ToggleGroup.Root>
{:else}
  <ToggleGroup.Root type="single" bind:value={() => props.type === "single" ? props.value : "", selectOne}
    variant={props.variant ?? "outline"} spacing={2} size={props.size ?? "default"} disabled={props.disabled}
    aria-label={props.ariaLabel} class={groupClass}>
    {@render choices()}
  </ToggleGroup.Root>
{/if}
