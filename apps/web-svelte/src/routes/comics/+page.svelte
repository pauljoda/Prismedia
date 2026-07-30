<script lang="ts">
  import { BookCopy } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.book}
  title="Comics"
  icon={BookCopy}
  prefsKey="comics"
  lockedServerQuery={{ bookType: "comic,manga" }}
  lockBookFilters
  emptyTitle="No comics"
  emptyMessage="No comics in your library yet. Add a library root with comic or manga files and scan to get started."
  errorMessage="Failed to load comics."
/>
