<script lang="ts">
  import type { SubtitleAppearance } from "$lib/settings/library-settings";
  import { cn } from "@prismedia/ui-svelte";
  import { captionClassName } from "$lib/player/subtitle-appearance";

  interface Props {
    text: string | null;
    appearance: SubtitleAppearance;
    class?: string;
    alwaysVisible?: boolean;
    placeholder?: string;
  }

  let {
    text,
    appearance,
    class: className,
    alwaysVisible = false,
    placeholder = "Example subtitle line",
  }: Props = $props();

  const display = $derived(text ?? placeholder);
</script>

{#if text || alwaysVisible}
  <div
    class={cn(
      "pointer-events-none absolute inset-x-0 z-10 flex justify-center px-4",
      className,
    )}
    style:top="{appearance.positionPercent}%"
    style:transform="translateY(-100%)"
    style:opacity={appearance.opacity}
  >
    <div
      class={cn(
        captionClassName(appearance.style),
        "max-w-[86%] whitespace-pre-line text-center font-medium leading-snug",
      )}
      style:font-size="{appearance.fontScale * 1.05}rem"
    >
      {display}
    </div>
  </div>
{/if}
