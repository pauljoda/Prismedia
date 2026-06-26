<script lang="ts">
  import { Feather } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="book-author"
  title="Authors"
  icon={Feather}
  prefsKey="authors"
  emptyTitle="No authors"
  emptyMessage="No authors in your collection yet. Organize books as Author/Title and scan to group them under an author."
  errorMessage="Failed to load authors."
/>
