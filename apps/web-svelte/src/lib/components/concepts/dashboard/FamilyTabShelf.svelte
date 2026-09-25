<script lang="ts">
  import { ChoiceGroup, Skeleton, type ChoiceOption } from "@prismedia/ui-svelte";
  import EntityShelf from "$lib/components/entities/EntityShelf.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { newestShelfParams, readShelfCards } from "./dashboard-data";
  import type { LibraryFamily } from "../library-composition";

  interface Props {
    /** Families in spectrum order; empty families are left out of the tabs. */
    families: LibraryFamily[];
    hideNsfw: boolean;
    /** Family to open first, such as the one that received the newest item. */
    initialKind?: string | null;
  }

  let { families, hideNsfw, initialKind = null }: Props = $props();

  const present = $derived(families.filter((family) => family.count > 0));
  let chosenKind = $state<string | null>(null);
  const selectedKind = $derived(
    present.find((family) => family.kind === chosenKind)?.kind ??
      present.find((family) => family.kind === initialKind)?.kind ??
      present[0]?.kind ??
      null,
  );
  const selected = $derived(present.find((family) => family.kind === selectedKind) ?? null);

  /** Shelves already read in this visit, keyed by NSFW mode and family, so switching tabs is instant. */
  let shelves = $state<Record<string, EntityThumbnailCard[]>>({});
  const shelfKey = $derived(selectedKind ? `${hideNsfw}:${selectedKind}` : null);
  const cards = $derived(shelfKey ? shelves[shelfKey] : undefined);

  const options = $derived<ChoiceOption[]>(
    present.map((family) => ({
      value: family.kind,
      label: family.label,
      icon: family.icon,
      iconColor: family.accent.primary,
      count: family.count,
    })),
  );

  $effect(() => {
    const key = shelfKey;
    const kind = selectedKind;
    const hide = hideNsfw;
    if (!key || !kind) return;
    let current = true;
    void readShelfCards(newestShelfParams(kind, hide), (next) => {
      if (current) shelves[key] = next;
    });
    return () => {
      current = false;
    };
  });
</script>

{#if present.length > 0}
  <section aria-labelledby="family-tabs-title" class="flex flex-col gap-4">
    <div class="flex flex-col gap-3 px-3">
      <h2 id="family-tabs-title" class="font-heading text-lg font-semibold text-text-primary">Newest</h2>
      <ChoiceGroup
        type="single"
        ariaLabel="Media family"
        size="sm"
        {options}
        value={selectedKind ?? ""}
        onValueChange={(kind) => (chosenKind = kind)}
      />
    </div>

    {#if selected && cards && cards.length > 0}
      <EntityShelf label={selected.label} icon={selected.icon} {cards} href={selected.href} />
    {:else if selected && cards}
      <p class="px-3 text-sm text-text-muted">No {selected.label.toLowerCase()}</p>
    {:else}
      <div class="flex gap-3 overflow-hidden px-3 pb-3" aria-hidden="true">
        {#each Array(7) as _, index (index)}
          <Skeleton class="h-48 w-36 flex-none rounded-[var(--radius-sm)]" />
        {/each}
      </div>
    {/if}
  </section>
{/if}
