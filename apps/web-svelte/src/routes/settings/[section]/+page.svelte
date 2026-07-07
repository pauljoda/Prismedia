<script lang="ts">
  import { page } from "$app/state";
  import SettingsSectionPage from "$lib/components/settings/SettingsSectionPage.svelte";

  const sectionId = $derived(page.params.section ?? "");
</script>

{#key sectionId}
  <SettingsSectionPage {sectionId} />
{/key}
