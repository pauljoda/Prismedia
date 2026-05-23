<script lang="ts">
  import UniversalLightbox from "./UniversalLightbox.svelte";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import type { UniversalLightboxEntity } from "./universal-lightbox-media";

  interface Props {
    entities: UniversalLightboxEntity[];
    initialIndex: number;
    onClose: () => void;
  }

  let {
    entities,
    initialIndex,
    onClose,
  }: Props = $props();

  provideNsfw(() => ({ initialMode: "show", lanAutoEnable: false }));
</script>

<UniversalLightbox
  {entities}
  {initialIndex}
  {onClose}
>
  {#snippet detailsContent(entity)}
    <section>
      <h3>Details for {entity.title}</h3>
    </section>
  {/snippet}
</UniversalLightbox>
