<script lang="ts">
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import { FolderOpen } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.videoSeries}
  title="Series"
  icon={FolderOpen}
  prefsKey="series"
  emptyTitle="No series"
  emptyMessage="No series in your library yet. Add a library root and scan to get started."
  errorMessage="Failed to load series."
/>
