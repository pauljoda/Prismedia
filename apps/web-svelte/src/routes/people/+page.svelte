<script lang="ts">
  import { Users } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="person"
  title="People"
  itemNoun="person"
  icon={Users}
  prefsKey="people"
  emptyTitle="No people"
  emptyMessage="No people in your library yet. They will appear as you tag media with credits."
  errorMessage="Failed to load people."
/>
