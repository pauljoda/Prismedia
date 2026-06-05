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
    showRatingControls?: boolean;
  }

  let {
    entities,
    initialIndex,
    onClose,
    onIndexChange,
    onRatingChange,
    showRatingControls = true,
  }: Props = $props();

  provideNsfw(() => ({ initialMode: "show", lanAutoEnable: false }));
</script>

<UniversalLightbox
  {entities}
  {initialIndex}
  {onClose}
  {onIndexChange}
  {onRatingChange}
  {showRatingControls}
/>
