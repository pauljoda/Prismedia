<script lang="ts">
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import { MicVocal } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.musicArtist}
  title="Artists"
  icon={MicVocal}
  prefsKey="artists"
  emptyTitle="No artists"
  emptyMessage="No artists in your collection yet. Organize music as Artist/Album/Songs and scan to group albums under an artist."
  errorMessage="Failed to load artists."
/>
