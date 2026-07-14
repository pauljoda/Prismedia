<script lang="ts">
  import { useNsfw } from "$lib/nsfw/store.svelte";

  interface Props {
    class?: string;
    size?: number;
    alt?: string;
    variant?: "color" | "neutral";
  }

  let { class: className, size = 28, alt = "Prismedia", variant = "color" }: Props = $props();
  const nsfw = useNsfw();
  const src = $derived.by(() => {
    if (variant === "neutral") return "/brand/prismedia-prism-neutral.png";
    return nsfw.mode === "show"
      ? "/brand/prismedia-prism-nsfw.png"
      : "/brand/prismedia-prism-color.png";
  });
  const dimensions = $derived(`width: ${size}px; height: ${size}px; object-fit: contain; display: block;`);
</script>

<img
  {src}
  {alt}
  width={size}
  height={size}
  class={className}
  style={dimensions}
  decoding="async"
  draggable="false"
/>
