<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { getCollection } from "$lib/api/generated/prismedia";
  import type { CollectionDetail } from "$lib/api/generated/model";
  import { unwrapGenerated } from "$lib/api/generated-response";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import CollectionEditor from "$lib/components/collections/CollectionEditor.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  let loadState: LoadState = $state("loading");
  let collection = $state<CollectionDetail | null>(null);
  let errorMessage = $state<string | null>(null);

  onMount(() => {
    void loadCollection();
  });

  async function loadCollection() {
    loadState = "loading";
    errorMessage = null;
    try {
      const id = page.params.id ?? "";
      collection = unwrapGenerated<CollectionDetail>(
        await getCollection(id),
        `Failed to fetch collection ${id}`,
      );
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
  <div class="error-notice">
    <p>{errorMessage ?? "Failed to load collection."}</p>
    <button type="button" onclick={() => void loadCollection()}>Retry</button>
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

  .error-notice button {
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-3);
    color: var(--color-text-muted);
    padding: 0.4rem 0.8rem;
  }

  @keyframes pulse {
    0%, 100% { opacity: 0.45; }
    50% { opacity: 0.85; }
  }
</style>
