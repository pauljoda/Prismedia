<script lang="ts">
  import { page } from "$app/state";
  import { Film, User } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchEntities, fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { RELATIONSHIP_CODE } from "$lib/api/generated/codes";
  import { getPersonProfileCapability } from "$lib/api/capabilities";
  import { entityCardToDetailCard, REFERENCE_STANDALONE_METADATA_SECTION_IDS, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityDetailActionButton,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  let relatedCards = $state<EntityThumbnailCard[]>([]);

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => page.params.id ?? "",
    load: async ({ signal }) => {
      const id = page.params.id ?? "";
      const nextPerson = await fetchEntity(id, { signal });
      try {
        const response = await fetchEntities(
          { referencedBy: id, relationshipCode: RELATIONSHIP_CODE.cast, limit: 1000 },
          { signal },
        );
        relatedCards = response.items.map((item) =>
          entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id)),
        );
      } catch (error) {
        if (signal.aborted) throw error;
        relatedCards = [];
      }
      return nextPerson;
    },
    breadcrumbs: (person) => [
      { label: "People", href: "/people" },
      { label: person.title },
    ],
  });

  const person = $derived(detail.entity);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!person) return null;
    return entityCardToDetailCard(person);
  });

  const identifyAction = useIdentifyDetailAction(() => person);
  const heroActions = $derived.by((): EntityDetailActionButton[] => identifyAction.action ? [identifyAction.action] : []);

  const dates = $derived(card?.dates ?? []);
  const profile = $derived(person ? getPersonProfileCapability(person.capabilities) : undefined);

  interface DetailRow { label: string; value: string }
  const bioRows = $derived.by((): DetailRow[] => {
    if (!profile) return [];
    const rows: DetailRow[] = [];
    if (profile.gender) rows.push({ label: "Gender", value: profile.gender });
    if (profile.country) rows.push({ label: "Country", value: profile.country });
    if (profile.ethnicity) rows.push({ label: "Ethnicity", value: profile.ethnicity });
    if (profile.eyeColor) rows.push({ label: "Eyes", value: profile.eyeColor });
    if (profile.hairColor) rows.push({ label: "Hair", value: profile.hairColor });
    if (profile.height != null) rows.push({ label: "Height", value: `${profile.height} cm` });
    if (profile.weight != null) rows.push({ label: "Weight", value: `${profile.weight} kg` });
    if (profile.measurements) rows.push({ label: "Measurements", value: profile.measurements });
    if (profile.tattoos) rows.push({ label: "Tattoos", value: profile.tattoos });
    if (profile.piercings) rows.push({ label: "Piercings", value: profile.piercings });
    if (profile.disambiguation) rows.push({ label: "Disambiguation", value: profile.disambiguation });
    return rows;
  });

</script>

<svelte:head>
  <title>{person?.title ?? "Person"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load person."
    onRetry={detail.retry}
  >
    {#if card && person}
      <EntityDetail
        {card}
        standaloneMetadataSectionIds={REFERENCE_STANDALONE_METADATA_SECTION_IDS}
        sections={[{ id: "tags", label: "Tags", editable: false }]}
        onRatingChange={detail.changeRating}
        onFavoriteToggle={detail.toggleFavorite}
        onOrganizedToggle={detail.toggleOrganized}
        onMetadataSave={detail.saveMetadata}
        ratingBusy={detail.ratingBusy}
        posterSize="large"
        actionButtons={heroActions}
      >
        {#snippet heroMeta()}
          {#if profile?.gender}
            <span class="meta-item">{profile.gender}</span>
          {/if}
          {#if profile?.country}
            {#if profile.gender}<span class="meta-sep"></span>{/if}
            <span class="meta-item">{profile.country}</span>
          {/if}
          <EntityDetailHeroDates {dates} leadingSeparator={Boolean(profile?.gender || profile?.country)} />
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
        <EntityGridSection
          title="Appearances"
          count={relatedCards.length}
          icon={Film}
          prefsKey={`person-${person.id}-appearances-section`}
        >
          <EntityGrid
            cards={relatedCards}
            prefsKey={`person-${person.id}-appearances`}
            emptyTitle="No appearances"
            emptyMessage="No content linked to this person."
          />
        </EntityGridSection>
      {/if}
    {/if}
  </EntityDetailPageState>
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

  /* Edge padding comes from EntityDetail's .detail-after-body. */

</style>
