<script lang="ts">
  import { Star } from "@lucide/svelte";
  import { ToggleButton, cn } from "@prismedia/ui-svelte";

  interface Props {
    value: number | null;
    max?: number;
    disabled?: boolean;
    compactLabels?: boolean;
    onChange?: (value: number | null) => void;
    readOnly?: boolean;
    ariaLabelPrefix?: string;
  }

  let {
    value,
    max = 5,
    disabled = false,
    compactLabels = false,
    onChange,
    readOnly = false,
    ariaLabelPrefix = "Set",
  }: Props = $props();

  const stars = $derived(value ? Math.max(0, Math.min(max, Math.round(value))) : 0);
  let hovered = $state(0);
</script>

{#if readOnly}
  <div class="flex items-center gap-0.5">
    {#each Array.from({ length: max }) as _, i (i)}
      <Star
        class={cn(
          "h-4 w-4",
          i < stars ? "fill-accent-500 text-accent-500" : "text-text-disabled",
        )}
      />
    {/each}
  </div>
{:else}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="flex items-center gap-0.5" onmouseleave={() => (hovered = 0)}>
    {#each Array.from({ length: max }) as _, i (i)}
      {@const starIdx = i + 1}
      {@const active = hovered > 0 ? starIdx <= hovered : starIdx <= stars}
      <ToggleButton variant="default" size="sm" class="size-7 p-0 data-[state=on]:bg-transparent" {disabled}
        onmouseenter={() => (hovered = starIdx)}
        bind:pressed={() => starIdx <= stars, () => {
          const newVal = starIdx === stars ? null : starIdx;
          onChange?.(newVal);
        }}
        aria-label={compactLabels ? `${ariaLabelPrefix} ${starIdx}` : `${ariaLabelPrefix} ${starIdx} star rating`}
      >
        <Star
          class={cn(
            "h-5 w-5 transition-colors duration-fast",
            active ? "fill-current text-[var(--detail-accent,var(--color-primary))]" : "text-muted-foreground",
          )}
        />
      </ToggleButton>
    {/each}
  </div>
{/if}
