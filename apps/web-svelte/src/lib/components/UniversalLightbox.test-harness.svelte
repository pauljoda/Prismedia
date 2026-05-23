<script lang="ts">
  import UniversalLightbox from "./UniversalLightbox.svelte";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import type { UniversalLightboxEntity } from "./universal-lightbox-media";

  interface Props {
    entities: UniversalLightboxEntity[];
    initialIndex: number;
    onClose: () => void;
    onIndexChange?: (index: number) => void;
    onRatingChange?: (entityId: string, rating: number | null) => void;
  }

  let {
    entities,
    initialIndex,
    onClose,
    onIndexChange,
    onRatingChange,
  }: Props = $props();

  provideNsfw(() => ({ initialMode: "show", lanAutoEnable: false }));
</script>

<UniversalLightbox
  {entities}
  {initialIndex}
  {onClose}
  {onIndexChange}
  {onRatingChange}
/>
