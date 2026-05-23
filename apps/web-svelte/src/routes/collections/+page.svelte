<script lang="ts">
  import { FolderOpen, Plus } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="collection"
  title="Collections"
  description="Curated groupings across your media"
  icon={FolderOpen}
  prefsKey="collections"
  emptyTitle="No collections"
  emptyMessage="No collections yet. Create one to group media across your library."
  errorMessage="Failed to load collections."
  actionHref="/collections/new"
  actionLabel="New Collection"
  actionIcon={Plus}
/>
