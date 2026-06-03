<script lang="ts">
  import { Tag as TagIcon } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="tag"
  title="Tags"
  itemNoun="tag"
  icon={TagIcon}
  prefsKey="tags"
  emptyTitle="No tags"
  emptyMessage="No tags in your library yet. They will appear as you tag your media."
  errorMessage="Failed to load tags."
/>
