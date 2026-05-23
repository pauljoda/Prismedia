<script lang="ts">
  import { Flame } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  interface Props {
    isNsfw?: boolean;
    class?: string;
    compact?: boolean;
  }

  let { isNsfw, class: className, compact = false }: Props = $props();
  const nsfw = useNsfw();
</script>

{#if isNsfw && nsfw.mode === "show"}
  <span
    class={cn(
      "inline-flex shrink-0 items-center justify-center border border-error/50 bg-error-muted/40 text-error-text shadow-[0_0_10px_rgba(168,72,80,0.35)]",
      compact ? "h-4 w-4" : "size-5",
      className,
    )}
    title="Marked NSFW"
    aria-label="NSFW"
  >
    <Flame class={compact ? "h-2.5 w-2.5" : "h-3 w-3"} strokeWidth={2.25} aria-hidden />
  </span>
{/if}
