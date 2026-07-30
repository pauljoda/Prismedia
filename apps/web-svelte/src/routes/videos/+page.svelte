<script lang="ts">
  import { Film } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.video}
  title="Videos"
  icon={Film}
  prefsKey="videos"
  emptyTitle="No videos"
  emptyMessage="No videos in your library yet. Add a library root and scan to get started."
  errorMessage="Failed to load videos."
  initialMediaWall
  enableFeedView
/>
