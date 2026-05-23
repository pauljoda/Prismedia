<script lang="ts">
  import { FolderOpen, Search } from "@lucide/svelte";

  interface DestinationItem {
    id: string;
    title: string;
    subtitle?: string | null;
  }

  interface Props {
    open: boolean;
    title: string;
    description: string;
    items: DestinationItem[];
    onConfirm: (id: string) => void | Promise<void>;
    onCancel: () => void;
  }

  let { open, title, description, items, onConfirm, onCancel }: Props = $props();
  let query = $state("");
  const filteredItems = $derived.by(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return items;
    return items.filter((item) => item.title.toLowerCase().includes(needle));
  });
</script>

{#if open}
  <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
    <button
      type="button"
      class="absolute inset-0 bg-black/80 backdrop-blur-sm"
      onclick={onCancel}
      aria-label="Cancel destination selection"
    ></button>
    <div
      role="dialog"
      aria-modal="true"
      aria-label={title}
      class="relative z-10 w-full max-w-lg surface-elevated p-6"
    >
      <div class="space-y-1.5">
        <h2 class="text-base font-heading font-semibold text-text-primary">{title}</h2>
        <p class="text-[0.78rem] leading-relaxed text-text-muted">{description}</p>
      </div>

      <div class="relative mt-4">
        <Search class="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-text-muted" />
        <input
          type="text"
          bind:value={query}
          class="w-full border border-border-subtle bg-surface-1 py-2 pl-8 pr-3 text-sm text-text-primary placeholder:text-text-muted focus:border-border-accent focus:outline-none"
          placeholder="Filter..."
        />
      </div>

      <div class="mt-3 max-h-[50vh] space-y-1.5 overflow-y-auto">
        {#if filteredItems.length === 0}
          <p class="py-6 text-center text-sm text-text-muted">No matching destinations</p>
        {:else}
          {#each filteredItems as item (item.id)}
            <button
              type="button"
              onclick={() => void onConfirm(item.id)}
              class="group flex w-full items-center gap-3 border border-border-subtle bg-surface-1 px-3.5 py-3 text-left transition-colors hover:border-border-accent hover:bg-surface-2"
            >
              <FolderOpen class="h-4 w-4 flex-shrink-0 text-text-muted group-hover:text-text-accent" />
              <span class="min-w-0 flex-1">
                <span class="block truncate text-sm font-medium text-text-primary">{item.title}</span>
                {#if item.subtitle}
                  <span class="block truncate text-[0.7rem] text-text-muted">{item.subtitle}</span>
                {/if}
              </span>
            </button>
          {/each}
        {/if}
      </div>

      <div class="mt-5 flex justify-end">
        <button
          type="button"
          onclick={onCancel}
          class="px-3 py-1.5 text-sm text-text-muted hover:bg-surface-2 hover:text-text-primary"
        >
          Cancel
        </button>
      </div>
    </div>
  </div>
{/if}
