<script lang="ts">
  import { FolderOpen, Plus } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.collection}
  title="Collections"
  icon={FolderOpen}
  prefsKey="collections"
  emptyTitle="No collections"
  emptyMessage="No collections yet. Create one to group media across your library."
  errorMessage="Failed to load collections."
  actionHref="/collections/new"
  actionLabel="New Collection"
  actionIcon={Plus}
/>
