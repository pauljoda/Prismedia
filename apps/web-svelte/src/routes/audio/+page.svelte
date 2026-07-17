<script lang="ts">
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import { Music } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.audioLibrary}
  title="Albums"
  icon={Music}
  prefsKey="audio"
  emptyTitle="No albums"
  emptyMessage="No albums in your collection yet. Add a library root and scan to get started."
  errorMessage="Failed to load albums."
/>
