<script lang="ts">
  import { FolderOpen } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="video-series"
  title="Series"
  description="Browse your series hierarchy and drill into seasons or episodes"
  icon={FolderOpen}
  prefsKey="series"
  emptyTitle="No series"
  emptyMessage="No series in your library yet. Add a library root and scan to get started."
  errorMessage="Failed to load series."
/>
