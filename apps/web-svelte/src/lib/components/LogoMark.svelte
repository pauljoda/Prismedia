<script lang="ts">
  import { useNsfw } from "$lib/nsfw/store.svelte";

  interface Props {
    class?: string;
    size?: number;
    alt?: string;
  }

  let { class: className, size = 28, alt = "Prismedia" }: Props = $props();
  const nsfw = useNsfw();

  const src = $derived(
    nsfw.mode === "show" ? "/brand/prismedia-logo-nsfw.png" : "/brand/prismedia-logo-mark.png",
  );
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
