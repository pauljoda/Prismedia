<script lang="ts">
  import { Star } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    value: number | null;
    onChange?: (value: number | null) => void;
    readOnly?: boolean;
    ariaLabelPrefix?: string;
  }

  let {
    value,
    onChange,
    readOnly = false,
    ariaLabelPrefix = "Set",
  }: Props = $props();

  const stars = $derived(value ? Math.round(value / 20) : 0);
  let hovered = $state(0);
</script>

{#if readOnly}
  <div class="flex items-center gap-0.5">
    {#each Array.from({ length: 5 }) as _, i (i)}
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
    {#each Array.from({ length: 5 }) as _, i (i)}
      {@const starIdx = i + 1}
      {@const active = hovered > 0 ? starIdx <= hovered : starIdx <= stars}
      <button
        type="button"
        onmouseenter={() => (hovered = starIdx)}
        onclick={() => {
          const newVal = starIdx === stars ? null : starIdx * 20;
          onChange?.(newVal);
        }}
        aria-label={`${ariaLabelPrefix} ${starIdx} star rating`}
        aria-pressed={active}
      >
        <Star
          class={cn(
            "h-5 w-5 transition-colors duration-fast",
            active ? "fill-accent-500 text-accent-500" : "text-text-disabled hover:text-accent-800",
          )}
        />
      </button>
    {/each}
  </div>
{/if}
