<script lang="ts">
  import { Film } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="video"
  title="Videos"
  icon={Film}
  prefsKey="videos"
  emptyTitle="No videos"
  emptyMessage="No videos in your library yet. Add a library root and scan to get started."
  errorMessage="Failed to load videos."
  initialMediaWall
  enableFeedView
  enableLightbox
  lightboxTitle="Videos"
/>
