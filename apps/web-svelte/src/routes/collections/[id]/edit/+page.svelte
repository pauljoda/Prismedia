<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { Button } from "@prismedia/ui-svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { getCollectionConfigurationCapability } from "$lib/api/capabilities";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import CollectionEditor from "$lib/components/collections/CollectionEditor.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  let loadState: LoadState = $state("loading");
  let collection = $state<EntityCardFull | null>(null);
  let errorMessage = $state<string | null>(null);

  onMount(() => {
    void loadCollection();
  });

  async function loadCollection() {
    loadState = "loading";
    errorMessage = null;
    try {
      const id = page.params.id ?? "";
      const loaded = await fetchEntity(id);
      if (!getCollectionConfigurationCapability(loaded.capabilities)?.canEdit) {
        await goto(resolve(`/collections/${id}` as "/"), { replaceState: true });
        return;
      }
      collection = loaded;
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }
</script>

{#if loadState === "loading"}
  <div class="loading-shell" aria-busy="true"></div>
{:else if loadState === "error"}
  <div class="error-notice" role="alert">
    <p>{errorMessage ?? "Failed to load collection."}</p>
    <Button variant="secondary" size="sm" onclick={() => void loadCollection()}>Retry</Button>
  </div>
{:else}
  <CollectionEditor {collection} />
{/if}

<style>
  .loading-shell {
    min-height: 28rem;
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-2);
    animation: pulse 1.2s ease-in-out infinite;
  }

  .error-notice {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    border: 1px solid color-mix(in srgb, var(--color-error) 50%, var(--color-border-subtle));
    background: var(--color-surface-2);
    color: var(--color-text-muted);
    padding: 1rem;
    font-size: 0.85rem;
  }

  @keyframes pulse {
    0%, 100% { opacity: 0.45; }
    50% { opacity: 0.85; }
  }
</style>
