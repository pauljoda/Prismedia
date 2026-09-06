<script lang="ts">
  import { Button, Command, Dialog } from "@prismedia/ui-svelte";
  import { FolderOpen } from "@lucide/svelte";

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
  <Dialog {open} onClose={onCancel} ariaLabel={title} class="w-full max-w-lg sm:max-w-lg p-6">

      <div class="space-y-1.5">
        <h2 class="text-base font-heading font-semibold text-text-primary">{title}</h2>
        <p class="text-[0.78rem] leading-relaxed text-text-muted">{description}</p>
      </div>

      <Command.Root shouldFilter={false} class="mt-4">
        <Command.Input bind:value={query} aria-label="Filter destinations" placeholder="Filter…" />

      <Command.List class="mt-3 max-h-[50vh]">
        <Command.Group>
        {#if filteredItems.length === 0}
          <p class="py-6 text-center text-sm text-text-muted">No matching destinations</p>
        {:else}
          {#each filteredItems as item (item.id)}
            <Command.Item value={item.id} onSelect={() => void onConfirm(item.id)} showIndicator={false} class="gap-3 py-3"
            >
              <FolderOpen class="h-4 w-4 flex-shrink-0 text-text-muted group-hover:text-text-accent" />
              <span class="min-w-0 flex-1">
                <span class="block truncate text-sm font-medium text-text-primary">{item.title}</span>
                {#if item.subtitle}
                  <span class="block truncate text-[0.7rem] text-text-muted">{item.subtitle}</span>
                {/if}
              </span>
            </Command.Item>
          {/each}
        {/if}
        </Command.Group>
      </Command.List>
      </Command.Root>

      <div class="mt-5 flex justify-end">
        <Button variant="ghost" onclick={onCancel}
        >
          Cancel
        </Button>
      </div>
  </Dialog>
{/if}
