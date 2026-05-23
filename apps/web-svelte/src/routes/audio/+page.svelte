<script lang="ts">
  import { Music } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="audio-library"
  title="Audio"
  description="Browse audio libraries in your collection"
  icon={Music}
  prefsKey="audio"
  emptyTitle="No audio"
  emptyMessage="No audio libraries in your collection yet. Add a library root and scan to get started."
  errorMessage="Failed to load audio."
/>
