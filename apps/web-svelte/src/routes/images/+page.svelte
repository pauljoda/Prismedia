<script lang="ts">
  import { Image as ImageIcon } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.image}
  title="Images"
  icon={ImageIcon}
  prefsKey="images"
  emptyTitle="No images"
  emptyMessage="No images in your library yet. Add a library root and scan to get started."
  errorMessage="Failed to load images."
  initialMediaWall
  enableFeedView
  enableLightbox
  lightboxTitle="Images"
/>
