<script lang="ts">
  import { page } from "$app/state";
  import ComicEntityDetailPage from "$lib/components/comics/ComicEntityDetailPage.svelte";
</script>

<ComicEntityDetailPage
  entityId={page.params.installmentId ?? ""}
  seriesId={page.params.id ?? ""}
/>
