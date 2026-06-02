<script lang="ts">
  import { MicVocal } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="music-artist"
  title="Artists"
  icon={MicVocal}
  prefsKey="artists"
  emptyTitle="No artists"
  emptyMessage="No artists in your collection yet. Organize music as Artist/Album/Songs and scan to group albums under an artist."
  errorMessage="Failed to load artists."
/>
