<script lang="ts">
  import { BookOpen } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.book}
  title="Books"
  icon={BookOpen}
  prefsKey="books"
  emptyTitle="No books"
  emptyMessage="No books in your library yet. Add a library root and scan to get started."
  errorMessage="Failed to load books."
/>
