<script lang="ts">
  import {
    Badge,
    BarChart3,
    Calendar,
    CheckCircle,
    Database,
    ExternalLink,
    Fingerprint,
    Flame,
    Heart,
    Link,
    ListOrdered,
    MonitorCog,
    Play,
    Star,
  } from "@lucide/svelte";
  import type { Snippet } from "svelte";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  import CreditsEditor from "$lib/components/forms/CreditsEditor.svelte";
  import EntityDatesEditor from "$lib/components/forms/EntityDatesEditor.svelte";
  import EntityPicker from "$lib/components/forms/EntityPicker.svelte";
  import FormField from "$lib/components/forms/FormField.svelte";
  import KeyValueEditor from "$lib/components/forms/KeyValueEditor.svelte";
  import ListEditor from "$lib/components/forms/ListEditor.svelte";
  import TextField from "$lib/components/forms/TextField.svelte";
  import ToggleChip from "$lib/components/forms/ToggleChip.svelte";
  import type { CreditRoleCode } from "$lib/entities/entity-codes";
  import {
    creditToThumbnailCard,
    type EntityDetailCard,
    type EntityDetailCardFull,
  } from "$lib/entities/entity-detail";
  import { validateUrl, type EntityDetailEditDraft } from "$lib/entities/entity-detail-edit";
  import {
    searchPeople,
    searchStudios,
  } from "$lib/entities/entity-detail-search";
  import EntityCastAndCrewSection from "./EntityCastAndCrewSection.svelte";
  import EntityDetailLinks from "./EntityDetailLinks.svelte";
  import type { EntityDetailSection } from "./entity-detail-types";

  interface Props {
    card: EntityDetailCard;
    defaultCreditRole: CreditRoleCode;
    draft: EntityDetailEditDraft;
    editing: boolean;
    onDraftChange: <Key extends keyof EntityDetailEditDraft>(
      key: Key,
      value: EntityDetailEditDraft[Key],
    ) => void;
    peopleLabel: string;
    section: EntityDetailSection;
    sectionContent?: Snippet<[EntityDetailSection]>;
  }

  let {
    card,
    defaultCreditRole,
    draft,
    editing,
    onDraftChange,
    peopleLabel,
    section,
    sectionContent,
  }: Props = $props();

  const cardFull = $derived(card as EntityDetailCard & Partial<EntityDetailCardFull>);
  const creditCards = $derived((cardFull.credits ?? []).map(creditToThumbnailCard));
  const studioCards = $derived(cardFull.studio ? [creditToThumbnailCard(cardFull.studio)] : []);
</script>

{#if editing && section.id === "links"}
  <section class="detail-section edit-section">
    <ListEditor
      values={draft.links}
      onChange={(value) => onDraftChange("links", value)}
      label="Links"
      placeholder="https://example.com"
      icon={Link}
      validate={validateUrl}
    />
    <KeyValueEditor
      values={draft.externalIds}
      onChange={(value) => onDraftChange("externalIds", value)}
      label="External IDs"
      icon={ExternalLink}
      keyPlaceholder="provider"
      valuePlaceholder="id"
      keyLabel="Provider"
      valueLabel="ID"
    />
  </section>
{:else if editing && section.id === "dates"}
  <section id="entity-dates-editor" class="detail-section edit-section">
    <EntityDatesEditor
      entityKind={card.entity.kind}
      values={draft.dates}
      onChange={(value) => onDraftChange("dates", value)}
    />
  </section>
{:else if editing && section.id === "stats"}
  <section class="detail-section edit-section">
    <KeyValueEditor
      values={draft.stats}
      onChange={(value) => onDraftChange("stats", value)}
      label="Stats"
      icon={BarChart3}
      keyPlaceholder="count"
      valuePlaceholder="12"
      keyLabel="Stat"
      valueLabel="Value"
      valueInputMode="decimal"
      validateValue={(value) => Number.isFinite(Number(value)) ? null : "Must be a number"}
    />
  </section>
{:else if editing && section.id === "positions"}
  <section class="detail-section edit-section">
    <KeyValueEditor
      values={draft.positions}
      onChange={(value) => onDraftChange("positions", value)}
      label="Positions"
      icon={ListOrdered}
      keyPlaceholder="sort"
      valuePlaceholder="1"
      keyLabel="Position"
      valueLabel="Value"
      valueInputMode="decimal"
      validateValue={(value) => Number.isFinite(Number(value)) ? null : "Must be a number"}
    />
  </section>
{:else if editing && section.id === "classification"}
  <section class="detail-section edit-section">
    <TextField
      value={draft.classification}
      onChange={(value) => onDraftChange("classification", value)}
      label="Classification"
      icon={Badge}
      placeholder="e.g. complete, draft, archived"
      helper="Empty clears the value"
    />
  </section>
{:else if editing && section.id === "rating"}
  <section class="detail-section edit-section">
    <TextField
      value={draft.ratingText}
      onChange={(value) => onDraftChange("ratingText", value)}
      label="Rating"
      icon={Star}
      helper="0 to {card.rating?.max ?? 5}; empty clears"
      type="number"
      min="0"
      max={card.rating?.max ?? 5}
    />
  </section>
{:else if editing && section.id === "flags"}
  <section class="detail-section edit-section">
    <FormField label="Flags">
      <div class="edit-flag-chips">
        <ToggleChip value={draft.isFavorite} onChange={(value) => onDraftChange("isFavorite", value)} onLabel="Favorite" icon={Heart} />
        <ToggleChip value={draft.isNsfw} onChange={(value) => onDraftChange("isNsfw", value)} onLabel="NSFW" variant="warning" icon={Flame} />
        <ToggleChip value={draft.isOrganized} onChange={(value) => onDraftChange("isOrganized", value)} onLabel="Organized" icon={CheckCircle} />
      </div>
    </FormField>
  </section>
{:else if editing && section.id === "studio"}
  <section class="detail-section edit-section">
    <EntityPicker
      values={draft.studioPick}
      onChange={(value) => onDraftChange("studioPick", value)}
      onSearch={searchStudios}
      label="Studio"
      placeholder="Search studios…"
      canAddNew={true}
      addNewLabel="studio"
      mode="single"
    />
  </section>
{:else if editing && section.id === "credits"}
  <section class="detail-section edit-section">
    <CreditsEditor
      credits={draft.credits}
      onChange={(value) => onDraftChange("credits", value)}
      onSearch={searchPeople}
      label={section.label ?? peopleLabel}
      defaultRole={defaultCreditRole}
    />
  </section>
{:else if section.id === "links"}
  <EntityDetailLinks links={card.links} />
{:else if section.id === "studio" && studioCards.length > 0}
  <section class="detail-section" aria-label={section.label ?? "Studio"}>
    <EntityCastAndCrewSection
      studioCards={studioCards}
      studioLabel={section.label ?? "Studio"}
    />
  </section>
{:else if section.id === "credits" && creditCards.length > 0}
  <section class="detail-section" aria-label={section.label ?? peopleLabel}>
    <EntityCastAndCrewSection
      creditCards={creditCards}
      castLabel={section.label ?? peopleLabel}
    />
  </section>
{:else if section.id === "stats" && (cardFull.stats?.length ?? 0) > 0}
  <MetadataCard
    title="Stats"
    icon={BarChart3}
    rows={(cardFull.stats ?? []).map((row) => ({ label: row.label, value: row.value }))}
  />
{:else if section.id === "dates" && (cardFull.dates?.length ?? 0) > 0}
  <MetadataCard
    title="Dates"
    icon={Calendar}
    rows={(cardFull.dates ?? []).map((row) => ({ label: row.label, value: row.display }))}
  />
{:else if section.id === "technical" && (cardFull.technical?.length ?? 0) > 0}
  <MetadataCard
    title="Technical"
    icon={MonitorCog}
    rows={(cardFull.technical ?? []).map((row) => ({ label: row.label, value: row.value }))}
  />
{:else if section.id === "progress" && cardFull.progress}
  <MetadataCard
    title="Progress"
    icon={Play}
    rows={[
      { label: "Progress", value: `${cardFull.progress.index} / ${cardFull.progress.total} ${cardFull.progress.unit}` },
      { label: "Percent", value: `${cardFull.progress.percent}%` },
      ...(cardFull.progress.mode ? [{ label: "Mode", value: cardFull.progress.mode }] : []),
    ]}
  />
{:else if section.id === "positions" && (cardFull.positions?.length ?? 0) > 0}
  <MetadataCard
    title="Positions"
    icon={ListOrdered}
    rows={(cardFull.positions ?? []).map((row) => ({ label: row.code, value: row.label }))}
  />
{:else if section.id === "classification" && cardFull.classification}
  <MetadataCard
    title="Classification"
    icon={Badge}
    rows={[{ label: cardFull.classification.label, value: cardFull.classification.value }]}
  />
{:else if section.id === "source" && ((cardFull.sources?.length ?? 0) > 0 || (cardFull.fingerprints?.length ?? 0) > 0)}
  <MetadataCard
    title="Source"
    icon={Database}
    rows={[
      ...(cardFull.sources ?? []).map((source) => ({ label: source.code, value: source.value })),
      ...(cardFull.fingerprints ?? []).map((fingerprint) => ({ label: String(fingerprint.algorithm), value: fingerprint.value })),
    ]}
  />
{:else if section.id === "sources" && (cardFull.sources?.length ?? 0) > 0}
  <MetadataCard
    title="Sources"
    icon={Database}
    rows={(cardFull.sources ?? []).map((source) => ({ label: source.code, value: source.value }))}
  />
{:else if section.id === "fingerprints" && (cardFull.fingerprints?.length ?? 0) > 0}
  <MetadataCard
    title="Fingerprints"
    icon={Fingerprint}
    rows={(cardFull.fingerprints ?? []).map((fingerprint) => ({ label: String(fingerprint.algorithm), value: fingerprint.value }))}
  />
{:else if sectionContent}
  <section class="detail-section custom-detail-section" aria-label={section.label ?? section.id}>
    {#if section.label}
      {@const SectionIcon = section.icon}
      <h2 class="section-label">
        {#if SectionIcon}
          <SectionIcon class="h-4 w-4" />
        {/if}
        {section.label}
      </h2>
    {/if}
    {@render sectionContent(section)}
  </section>
{/if}

<style>
  .detail-section {
    min-width: 0;
    padding: 0;
    border-bottom: none;
  }

  .edit-section {
    display: grid;
    gap: 0.75rem;
  }

  .edit-flag-chips {
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem;
    min-height: 2.55rem;
    align-items: center;
  }

  .edit-flag-chips :global(button) {
    min-height: 2.55rem;
  }

  .section-label {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    margin: 0 0 0.75rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--detail-text-muted);
  }
</style>
