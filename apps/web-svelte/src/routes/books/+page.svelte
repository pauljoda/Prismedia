<script lang="ts">
  import { BookOpen } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="book"
  title="Books"
  description="Browse comics and books in your library"
  icon={BookOpen}
  prefsKey="books"
  emptyTitle="No books"
  emptyMessage="No books in your library yet. Add a library root and scan to get started."
  errorMessage="Failed to load books."
/>
