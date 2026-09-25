<script lang="ts">
  import { Check, FolderPlus, Loader2, Plus } from "@lucide/svelte";
  import { Button, Command, Popover, buttonVariants } from "@prismedia/ui-svelte";
  import { COLLECTION_MODE } from "$lib/api/generated/codes";
  import { addCollectionItems, createCollection, fetchAddableCollections } from "$lib/api/collections";
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
  let creating = $state(false);
  let lastResult = $state<{ id: string; title: string; count: number } | null>(null);

  /** A typed name that matches no existing collection can become a new one. */
  const newTitle = $derived(query.trim());
  const canCreate = $derived(
    newTitle.length > 0 &&
      !collections.some((collection) => collection.title.localeCompare(newTitle, undefined, { sensitivity: "accent" }) === 0),
  );

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
      collections = await fetchAddableCollections();
      loadState = "ready";
    } catch (err) {
      errorMessage = err instanceof Error ? err.message : "Failed to load collections.";
      loadState = "error";
    }
  }

  function setOpen(next: boolean) {
    open = next;
    if (open) {
      lastResult = null;
      errorMessage = null;
      if (loadState === "idle" || loadState === "error") void loadCollections();
    }
  }


  /** Creates a manual collection from the typed name and adds the selection to it. */
  async function createAndAdd() {
    if (creating || pendingId || items.length === 0 || !canCreate) return;
    creating = true;
    errorMessage = null;
    lastResult = null;
    try {
      const created = await createCollection({ title: newTitle, mode: COLLECTION_MODE.manual });
      const collection = { id: created.id, title: created.title };
      collections = [collection, ...collections];
      query = "";
      creating = false;
      await addTo(collection);
    } catch (err) {
      errorMessage = err instanceof Error ? err.message : "Failed to create collection.";
    } finally {
      creating = false;
    }
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

<Popover.Root {open} onOpenChange={setOpen}>
  <Popover.Trigger class={buttonVariants({ variant: "outline", size: "sm" })} aria-label="Add selection to a collection">
    <FolderPlus />
    <span class="hidden min-[520px]:inline">Add to Collection</span>
  </Popover.Trigger>
  <Popover.Content align="end" class="w-72 p-0">
    <Command.Root shouldFilter={false}>
      <Command.Input placeholder="Find or name a collection…" aria-label="Find or name a collection" bind:value={query} />
      <p class="px-3 py-2 text-xs text-muted-foreground">Add {items.length} {items.length === 1 ? "item" : "items"} to…</p>
      {#if loadState === "loading"}
        <p role="status" class="flex items-center gap-2 px-3 py-4 text-sm text-muted-foreground"><Loader2 class="animate-spin" />Loading collections…</p>
      {:else if loadState === "error"}
        <div class="space-y-2 p-3">
          <p role="alert" class="text-sm text-destructive">{errorMessage ?? "Failed to load collections."}</p>
          <Button variant="outline" size="sm" onkeydown={event => event.stopPropagation()} onclick={() => void loadCollections()}>Retry</Button>
        </div>
      {:else}
        {#if lastResult}
          <p role="status" class="flex items-center gap-2 px-3 py-2 text-sm"><Check />Added {lastResult.count} to {lastResult.title}</p>
        {/if}
        {#if errorMessage}<p role="alert" class="px-3 py-2 text-sm text-destructive">{errorMessage}</p>{/if}
        <Command.List>
          {#if canCreate}
            <Command.Group>
              <Command.Item value="create-collection" disabled={creating || pendingId !== null} onSelect={() => void createAndAdd()}>
                {#if creating}<Loader2 class="animate-spin" />{:else}<Plus />{/if}
                <span class="min-w-0 flex-1 truncate">New collection “{newTitle}”</span>
              </Command.Item>
            </Command.Group>
          {/if}
          <Command.Group>
            {#each filtered as collection (collection.id)}
              <Command.Item value={collection.id} disabled={pendingId !== null} onSelect={() => void addTo(collection)}>
                <span class="min-w-0 flex-1 truncate">{collection.title}</span>
                {#if pendingId === collection.id}<Loader2 class="animate-spin" />
                {:else if lastResult?.id === collection.id}<Check />{/if}
              </Command.Item>
            {:else}
              {#if !canCreate}
                <p class="px-3 py-4 text-center text-sm text-muted-foreground">{collections.length ? "No matches." : "No collections yet."}</p>
              {/if}
            {/each}
          </Command.Group>
        </Command.List>
      {/if}
    </Command.Root>
  </Popover.Content>
</Popover.Root>
