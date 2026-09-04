<script lang="ts">
  import { Badge, buttonVariants, cn, ToggleButton } from "@prismedia/ui-svelte";
  import StarRatingPicker from "../StarRatingPicker.svelte";
  import { CheckCircle, ExternalLink, Flame, Heart, Pencil, PencilOff } from "@lucide/svelte";
  import type { Snippet } from "svelte";
  import type { EntityDetailCard } from "$lib/entities/entity-detail";
  import EntityActionButton from "./EntityActionButton.svelte";
  import type { EntityDetailActionButton, EntityDetailProps } from "./entity-detail-types";

  interface Props {
    actionButtons: EntityDetailActionButton[];
    canEdit: boolean;
    cancelEditActionLabel: string;
    card: EntityDetailCard;
    editActionLabel: string;
    editing: boolean;
    heroBadges?: Snippet;
    isFavorite: boolean;
    isNsfw: boolean;
    isOrganized: boolean;
    onCancelEdit: () => void;
    onFavoriteToggle?: EntityDetailProps["onFavoriteToggle"];
    onOrganizedToggle?: EntityDetailProps["onOrganizedToggle"];
    onRatingChange?: EntityDetailProps["onRatingChange"];
    onStartEdit: () => void;
    ratingBusy: boolean;
    savingEdit: boolean;
    showFlagActions: boolean;
  }

  let {
    actionButtons,
    canEdit,
    cancelEditActionLabel,
    card,
    editActionLabel,
    editing,
    heroBadges,
    isFavorite,
    isNsfw,
    isOrganized,
    onCancelEdit,
    onFavoriteToggle,
    onOrganizedToggle,
    onRatingChange,
    onStartEdit,
    ratingBusy,
    savingEdit,
    showFlagActions,
  }: Props = $props();

  const providerIdentityLabel = $derived(card.providerIdentity?.pluginId ?? "");
  const providerIdentityTitle = $derived.by(() => {
    const identity = card.providerIdentity;
    if (!identity) return "";
    return `Metadata and monitoring source: ${identity.pluginId}, ${identity.identityNamespace} ID ${identity.identityValue}`;
  });

</script>

{#if card.rating}
  <div class="rating-row" role="group" aria-label="Rating">
    <StarRatingPicker value={card.rating.value} max={card.rating.max} disabled={ratingBusy || !onRatingChange} onChange={onRatingChange} ariaLabelPrefix="Rate" compactLabels />
  </div>
{/if}

{#if card.providerIdentity || heroBadges}
  <div class="position-badges">
    {#if card.providerIdentity}
      {#if card.providerIdentity.url}
        <a
          href={card.providerIdentity.url}
          target="_blank"
          rel="noopener noreferrer"
          class={cn(buttonVariants({ variant: "outline", size: "default" }), "min-w-0 max-w-full")}
          title={providerIdentityTitle}
          aria-label={`${providerIdentityTitle}. Opens provider in a new tab.`}
        >
          <span class="truncate">{providerIdentityLabel}</span>
          <ExternalLink data-icon="inline-end" aria-hidden="true" />
        </a>
      {:else}
        <Badge
          variant="outline"
          title={providerIdentityTitle}
          aria-label={providerIdentityTitle}
        >
          <span class="truncate">{providerIdentityLabel}</span>
        </Badge>
      {/if}
    {/if}
    {#if heroBadges}
      {@render heroBadges()}
    {/if}
  </div>
{/if}

{#if showFlagActions || canEdit || actionButtons.length > 0}
  <div class="action-row">
    <div class="action-badges">
      {#if showFlagActions}
        <ToggleButton variant="outline" class="size-control p-0 data-[state=on]:text-error-text" bind:pressed={() => isFavorite, () => onFavoriteToggle?.()} disabled={!onFavoriteToggle} aria-label={isFavorite ? "Remove from favorites" : "Add to favorites"}
        >
          <Heart class="h-4 w-4" />
        </ToggleButton>

        {#if isNsfw}
          <Badge variant="error" aria-label="NSFW">
            <Flame /> NSFW
          </Badge>
        {/if}

        <ToggleButton variant="outline" class="size-control p-0 data-[state=on]:text-success-text" bind:pressed={() => isOrganized, () => onOrganizedToggle?.()} disabled={!onOrganizedToggle} aria-label={isOrganized ? "Mark as unorganized" : "Mark as organized"}
        >
          <CheckCircle class="h-4 w-4" />
        </ToggleButton>
      {/if}
    </div>

    {#if canEdit || actionButtons.length > 0}
      <div class="action-group">
        {#if canEdit}
          {#if editing}
            <EntityActionButton
              label="Editing"
              icon={PencilOff}
              active
              disabled={savingEdit}
              ariaLabel={cancelEditActionLabel}
              onClick={onCancelEdit}
            />
          {:else}
            <EntityActionButton
              label="Edit"
              icon={Pencil}
              ariaLabel={editActionLabel}
              onClick={onStartEdit}
            />
          {/if}
        {/if}
        {#each actionButtons as action (action.id)}
          {#if action.href && !action.disabled}
            <EntityActionButton
              label={action.label}
              icon={action.icon}
              href={action.href}
              active={action.active}
              variant={action.variant ?? "default"}
              iconClass={action.iconClass}
              iconFill={action.iconFill}
              ariaLabel={action.ariaLabel ?? action.label}
              title={action.title ?? action.ariaLabel ?? action.label}
            />
          {:else if action.disabled && action.disabledHint}
            <span class="entity-action-flyout-host">
              <EntityActionButton
                label={action.label}
                icon={action.icon}
                muted
                ariaDisabled
                variant={action.variant ?? "default"}
                iconClass={action.iconClass}
                iconFill={action.iconFill}
                ariaLabel={action.ariaLabel ?? action.label}
              />
              <span class="entity-action-flyout" role="tooltip">{action.disabledHint}</span>
            </span>
          {:else}
            <EntityActionButton
              label={action.label}
              icon={action.icon}
              active={action.active}
              variant={action.variant ?? "default"}
              disabled={action.disabled}
              iconClass={action.iconClass}
              iconFill={action.iconFill}
              ariaLabel={action.ariaLabel ?? action.label}
              title={action.title ?? action.ariaLabel ?? action.label}
              onClick={action.onClick}
            />
          {/if}
        {/each}
      </div>
    {/if}
  </div>
{/if}

<style>
  .rating-row {
    display: flex;
    gap: var(--spacing-control-gap-sm);
  }



  .position-badges {
    display: flex;
    align-items: center;
    gap: var(--spacing-control-gap);
    flex-wrap: wrap;
  }

  .action-row {
    display: flex;
    flex-wrap: wrap;
    align-items: flex-start;
    justify-content: space-between;
    gap: var(--spacing-control-gap);
    width: 100%;
  }

  .action-badges {
    display: flex;
    align-items: center;
    gap: var(--spacing-control-gap);
  }

  .action-group {
    display: flex;
    flex: 1 1 auto;
    flex-wrap: wrap;
    min-width: 0;
    align-items: center;
    justify-content: flex-end;
    gap: var(--spacing-control-gap);
    margin-left: auto;
  }

  @media (max-width: 767px) {
    .rating-row { grid-column: var(--detail-text-column, 2); grid-row: 3; min-width: 0; }
    .position-badges {
      grid-column: var(--detail-text-column, 2);
      grid-row: 4;
      justify-self: stretch;
      justify-content: flex-start;
      width: 100%;
    }
    .action-row {
      grid-column: 1 / -1;
      grid-row: 5;
      flex-direction: column;
      align-items: stretch;
      gap: var(--spacing-control-gap);
      margin-top: var(--spacing-control-gap);
    }
    .action-badges { justify-self: start; }
    .action-group {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--spacing-control-gap);
      width: 100%;
      margin-left: 0;
    }
    .action-group :global(.entity-detail-action),
    .action-group :global(.entity-action-flyout-host) { width: 100%; min-width: 0; }
    .action-group > :global(:last-child:nth-child(odd)) { grid-column: 1 / -1; }
  }
  @media (max-width: 400px) {
    .rating-row, .position-badges { grid-column: 1 / -1; }
  }
</style>
