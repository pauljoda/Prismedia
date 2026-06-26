<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Film, Layers, BookOpen, Music, User } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { fetchEntities } from "$lib/api/entities";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { getPerson } from "$lib/api/generated/prismedia";
  import type { PersonDetail } from "$lib/api/generated/model";
  import { unwrapGenerated } from "$lib/api/generated-response";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { entityCardToDetailCard, REFERENCE_STANDALONE_METADATA_SECTION_IDS, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let person = $state<PersonDetail | null>(null);
  let relatedCards = $state<EntityThumbnailCard[]>([]);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!person) return null;
    return entityCardToDetailCard(person);
  });

  const identifyAction = useIdentifyDetailAction(() => card?.entity.id, () => card?.entity.kind);
  const heroActions = $derived.by((): EntityDetailActionButton[] => identifyAction.action ? [identifyAction.action] : []);

  const dates = $derived(card?.dates ?? []);

  interface DetailRow { label: string; value: string }
  const bioRows = $derived.by((): DetailRow[] => {
    if (!person) return [];
    const rows: DetailRow[] = [];
    if (person.gender) rows.push({ label: "Gender", value: person.gender });
    if (person.country) rows.push({ label: "Country", value: person.country });
    if (person.ethnicity) rows.push({ label: "Ethnicity", value: person.ethnicity });
    if (person.eyeColor) rows.push({ label: "Eyes", value: person.eyeColor });
    if (person.hairColor) rows.push({ label: "Hair", value: person.hairColor });
    if (person.height != null) rows.push({ label: "Height", value: `${person.height} cm` });
    if (person.weight != null) rows.push({ label: "Weight", value: `${person.weight} kg` });
    if (person.measurements) rows.push({ label: "Measurements", value: person.measurements });
    if (person.tattoos) rows.push({ label: "Tattoos", value: person.tattoos });
    if (person.piercings) rows.push({ label: "Piercings", value: person.piercings });
    if (person.disambiguation) rows.push({ label: "Disambiguation", value: person.disambiguation });
    return rows;
  });

  onMount(() => {
    void loadPerson();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadPerson();
  });

  $effect(() => {
    if (!person) return;
    return appChrome.setBreadcrumbs([
      { label: "People", href: "/people" },
      { label: person.title },
    ]);
  });

  async function loadPerson() {
    loadState = "loading";
    errorMessage = null;
    try {
      const id = page.params.id ?? "";
      person = unwrapGenerated<PersonDetail>(await getPerson(id), `Failed to fetch person ${id}`);
      await loadRelated(id);
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function loadRelated(personId: string) {
    try {
      const response = await fetchEntities({ referencedBy: personId, relationshipCode: "cast", limit: 1000 });
      relatedCards = response.items.map((item) => entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id)));
    } catch {
      relatedCards = [];
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!person || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(person, value, (next) => (person = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!person) return;
    await toggleOptimisticEntityFlag(person, "isFavorite", (next) => (person = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!person) return;
    await toggleOptimisticEntityFlag(person, "isOrganized", (next) => (person = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!person) return;
    await updateEntityMetadata(person.id, request, { kind: person.kind });
    await loadPerson();
  }
</script>

<svelte:head>
  <title>{person?.title ?? "Person"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load person."}</p>
      <button type="button" onclick={() => void loadPerson()}>Retry</button>
    </div>
  {:else if card && person}
    <EntityDetail
      {card}
      standaloneMetadataSectionIds={REFERENCE_STANDALONE_METADATA_SECTION_IDS}
      sections={[{ id: "tags", label: "Tags", editable: false }]}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      posterSize="large"
      actionButtons={heroActions}
    >
      {#snippet heroMeta()}
        {#if person?.gender}
          <span class="meta-item">{person.gender}</span>
        {/if}
        {#if person?.country}
          {#if person.gender}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{person.country}</span>
        {/if}
        <EntityDetailHeroDates {dates} leadingSeparator={Boolean(person?.gender || person?.country)} />
      {/snippet}


      {#snippet afterBody()}
        {#if bioRows.length > 0}
          <div class="bio-section">
            <MetadataCard title="Details" icon={User} rows={bioRows} />
          </div>
        {/if}
      {/snippet}
    </EntityDetail>

    {#if relatedCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <Film class="h-4 w-4" />
          Appearances
          <span class="content-count">{relatedCards.length}</span>
        </h2>
        <EntityGrid
          cards={relatedCards}
          prefsKey={`person-${person?.id}-appearances`}
          emptyTitle="No appearances"
          emptyMessage="No content linked to this person."
        />
      </section>
    {/if}
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

  /* Edge padding comes from EntityDetail's .detail-after-body. */

  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

</style>
