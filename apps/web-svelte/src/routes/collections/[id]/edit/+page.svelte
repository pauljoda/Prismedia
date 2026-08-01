<script lang="ts">
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { getCollectionConfigurationCapability } from "$lib/api/capabilities";
  import CollectionEditor from "$lib/components/collections/CollectionEditor.svelte";

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => page.params.id ?? "",
    reloadOnNsfwChange: false,
    load: async ({ signal }) => {
      const id = page.params.id ?? "";
      const loaded = await fetchEntity(id, { signal });
      if (!getCollectionConfigurationCapability(loaded.capabilities)?.canEdit) {
        await goto(resolve(`/collections/${id}` as "/"), { replaceState: true });
        signal.throwIfAborted();
      }
      return loaded;
    },
  });

  const collection = $derived(detail.entity);
</script>

<EntityDetailPageState
  loadState={detail.loadState}
  errorMessage={detail.errorMessage}
  fallbackError="Failed to load collection."
  onRetry={detail.retry}
  showHero={false}
>
  {#if collection}
    <CollectionEditor {collection} />
  {/if}
</EntityDetailPageState>
