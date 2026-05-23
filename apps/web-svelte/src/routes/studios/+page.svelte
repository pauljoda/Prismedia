<script lang="ts">
  import { Building2 } from "@lucide/svelte";
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
</script>

<EntityIndexPage
  kind="studio"
  title="Studios"
  description="Browse studios in your library"
  icon={Building2}
  prefsKey="studios"
  emptyTitle="No studios"
  emptyMessage="No studios in your library yet. They will appear as you tag media with studio metadata."
  errorMessage="Failed to load studios."
/>
