<script lang="ts">
  import { Layers } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="gallery"
  title="Galleries"
  icon={Layers}
  prefsKey="galleries"
  emptyTitle="No galleries"
  emptyMessage="No galleries in your library yet. Add a library root and scan to get started."
  errorMessage="Failed to load galleries."
/>
