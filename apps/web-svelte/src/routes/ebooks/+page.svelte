<script lang="ts">
  import { BookMarked } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.book}
  title="eBooks"
  icon={BookMarked}
  prefsKey="ebooks"
  lockedServerQuery={{ bookType: "book,novel", bookFormat: "epub,pdf" }}
  lockBookFilters
  emptyTitle="No eBooks"
  emptyMessage="No eBooks in your library yet. Add a library root with EPUB or PDF files and scan to get started."
  errorMessage="Failed to load eBooks."
/>
