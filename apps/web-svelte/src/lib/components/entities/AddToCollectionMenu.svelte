<script lang="ts">
  import { AlertCircle, Check, FolderPlus, Loader2, Search } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import { addCollectionItems, fetchCollections } from "$lib/api/collections";
  import type { CollectionEntityType } from "$lib/collections/models";

  interface CollectionOption {
    id: string;
    title: string;
  }

  interface Props {
    /** Collection-eligible members resolved from the current selection. */
    items: { entityType: CollectionEntityType; entityId: string }[];
  }

  let { items }: Props = $props();

  type LoadState = "idle" | "loading" | "ready" | "error";

  let open = $state(false);
  let loadState = $state<LoadState>("idle");
  let collections = $state<CollectionOption[]>([]);
  let query = $state("");
  let errorMessage = $state<string | null>(null);
  let pendingId = $state<string | null>(null);
  let lastResult = $state<{ id: string; title: string; count: number } | null>(null);

  const filtered = $derived.by(() => {
    const term = query.trim().toLowerCase();
    if (!term) return collections;
    return collections.filter((collection) => collection.title.toLowerCase().includes(term));
  });

  async function loadCollections() {
    if (loadState === "loading") return;
    loadState = "loading";
    errorMessage = null;
    try {
      const response = await fetchCollections();
      collections = response.items.map((item) => ({ id: item.id, title: item.title }));
      loadState = "ready";
    } catch (err) {
      errorMessage = err instanceof Error ? err.message : "Failed to load collections.";
      loadState = "error";
    }
  }

  function toggleOpen() {
    open = !open;
    if (open) {
      lastResult = null;
      errorMessage = null;
      if (loadState === "idle" || loadState === "error") void loadCollections();
    }
  }

  function close() {
    open = false;
  }

  async function addTo(collection: CollectionOption) {
    if (pendingId || items.length === 0) return;
    pendingId = collection.id;
    errorMessage = null;
    lastResult = null;
    try {
      const response = await addCollectionItems(collection.id, { items });
      lastResult = { id: collection.id, title: collection.title, count: response.count };
    } catch (err) {
      errorMessage = err instanceof Error ? err.message : "Failed to add to collection.";
    } finally {
      pendingId = null;
    }
  }
</script>

<div class="atc-menu">
  <button
    type="button"
    class={cn("bulk-btn", open && "is-active")}
    title="Add selection to a collection"
    aria-label="Add selection to a collection"
    aria-expanded={open}
    onclick={toggleOpen}
  >
    <FolderPlus class="h-3.5 w-3.5" />
    <span class="bulk-btn-label">Add to Collection</span>
  </button>

  {#if open}
    <button
      type="button"
      class="fixed inset-0 z-40 cursor-default"
      aria-label="Close add to collection menu"
      onclick={close}
    ></button>
    <div class="atc-flyout" use:keepFlyoutOnScreen>
      <div class="atc-kicker">
        Add {items.length} {items.length === 1 ? "item" : "items"} to…
      </div>

      {#if collections.length > 6}
        <label class="atc-search">
          <Search class="h-3.5 w-3.5 shrink-0 text-text-disabled" />
          <input
            type="search"
            placeholder="Filter collections…"
            bind:value={query}
          />
        </label>
      {/if}

      {#if loadState === "loading"}
        <div class="atc-status">
          <Loader2 class="h-3.5 w-3.5 animate-spin" />
          Loading collections…
        </div>
      {:else if loadState === "error"}
        <div class="atc-status atc-status-error">
          <AlertCircle class="h-3.5 w-3.5" />
          {errorMessage ?? "Failed to load collections."}
        </div>
        <button type="button" class="atc-retry" onclick={() => void loadCollections()}>Retry</button>
      {:else if collections.length === 0}
        <div class="atc-status">No collections yet.</div>
      {:else}
        {#if lastResult}
          <div class="atc-result">
            <Check class="h-3.5 w-3.5 shrink-0" />
            Added {lastResult.count} to {lastResult.title}
          </div>
        {/if}
        {#if errorMessage}
          <div class="atc-status atc-status-error">
            <AlertCircle class="h-3.5 w-3.5" />
            {errorMessage}
          </div>
        {/if}
        <div class="atc-list">
          {#each filtered as collection (collection.id)}
            <button
              type="button"
              class="atc-item"
              disabled={pendingId !== null}
              onclick={() => addTo(collection)}
            >
              <span class="atc-item-title">{collection.title}</span>
              {#if pendingId === collection.id}
                <Loader2 class="h-3 w-3 shrink-0 animate-spin text-text-accent" />
              {:else if lastResult?.id === collection.id}
                <Check class="h-3 w-3 shrink-0 text-text-accent" />
              {/if}
            </button>
          {:else}
            <div class="atc-status">No matches.</div>
          {/each}
        </div>
      {/if}
    </div>
  {/if}
</div>

<style>
  .atc-menu {
    position: relative;
  }

  /*
   * The trigger mirrors the toolbar's `.bulk-btn` recipe. The bulk-bar styles
   * live in EntityGridToolbar's scoped stylesheet, so the family is repeated
   * here to keep this self-contained menu visually identical to its siblings.
   */
  .bulk-btn {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    height: 1.85rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    letter-spacing: 0.04em;
    padding: 0 0.55rem;
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.30);
    transition:
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .bulk-btn:hover {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
    box-shadow: 0 0 0 1px rgba(199, 201, 204, 0.35), 0 0 8px rgba(199, 201, 204, 0.15);
  }

  .bulk-btn:focus-visible {
    outline: none;
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    box-shadow: 0 0 0 1px rgba(199, 201, 204, 0.35), 0 0 8px rgba(199, 201, 204, 0.15);
  }

  .bulk-btn.is-active {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    background: var(--color-surface-4, #1c2235);
    color: var(--color-text-accent, #c7c9cc);
    box-shadow: 0 0 0 1px rgba(199, 201, 204, 0.35), 0 0 8px rgba(199, 201, 204, 0.15);
  }

  .bulk-btn-label {
    display: none;
  }

  @media (min-width: 520px) {
    .bulk-btn-label {
      display: inline;
    }
  }

  .atc-flyout {
    position: absolute;
    right: 0;
    top: calc(100% + 0.3rem);
    z-index: 50;
    display: flex;
    flex-direction: column;
    gap: 0.35rem;
    width: min(15rem, calc(100vw - 4rem));
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgba(12, 15, 21, 0.98);
    backdrop-filter: blur(var(--glass-blur-lg));
    -webkit-backdrop-filter: blur(var(--glass-blur-lg));
    border-radius: var(--radius-sm, 6px);
    box-shadow: 0 8px 40px rgba(0, 0, 0, 0.60);
    padding: 0.5rem;
  }

  .atc-kicker {
    padding: 0 0.25rem;
    color: var(--color-text-disabled);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
    font-weight: 600;
    letter-spacing: 0.1em;
    text-transform: uppercase;
  }

  .atc-search {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    height: 1.9rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-1, #0c0f15);
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.30);
    padding: 0 0.5rem;
  }

  .atc-search:focus-within {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.30), 0 0 0 1px rgba(199, 201, 204, 0.35);
  }

  .atc-search input {
    min-width: 0;
    width: 100%;
    border: 0;
    background: transparent;
    color: var(--color-text-primary);
    font-family: var(--font-body, Inter, sans-serif);
    font-size: 0.78rem;
    outline: 0;
  }

  .atc-search input::placeholder {
    color: var(--color-text-disabled);
  }

  .atc-search input::-webkit-search-cancel-button {
    appearance: none;
    -webkit-appearance: none;
  }

  .atc-list {
    display: flex;
    flex-direction: column;
    gap: 0.1rem;
    max-height: 14rem;
    overflow-y: auto;
    scrollbar-width: thin;
  }

  .atc-item {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    width: 100%;
    padding: 0.45rem 0.55rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid transparent;
    background: transparent;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    letter-spacing: 0.02em;
    text-align: left;
    transition:
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .atc-item:not(:disabled):hover {
    background: rgb(255 255 255 / 0.04);
    border-color: var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    color: var(--color-text-primary);
  }

  .atc-item:disabled {
    cursor: progress;
    opacity: 0.7;
  }

  .atc-item-title {
    flex: 1 1 auto;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .atc-status {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    padding: 0.5rem 0.55rem;
    color: var(--color-text-disabled);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
  }

  .atc-status-error {
    color: var(--color-error-text, #cc7880);
  }

  .atc-retry {
    align-self: flex-start;
    padding: 0.3rem 0.6rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    transition: color var(--duration-fast) var(--ease-default), border-color var(--duration-fast) var(--ease-default);
  }

  .atc-retry:hover {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    color: var(--color-text-primary);
  }

  .atc-result {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    padding: 0.4rem 0.55rem;
    border-radius: var(--radius-xs, 4px);
    background: linear-gradient(90deg, rgb(199 201 204 / 0.12), transparent);
    border: 1px solid rgb(199 201 204 / 0.18);
    color: var(--color-text-accent, #c49a5a);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
  }
</style>
