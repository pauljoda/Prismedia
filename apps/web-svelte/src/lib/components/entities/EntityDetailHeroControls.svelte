<script lang="ts">
  import { ToggleButton } from "@prismedia/ui-svelte";
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
          class="hero-badge provider-identity-chip"
          title={providerIdentityTitle}
          aria-label={`${providerIdentityTitle}. Opens provider in a new tab.`}
        >
          <span class="provider-identity-label">{providerIdentityLabel}</span>
          <ExternalLink class="provider-identity-link-icon h-3 w-3" aria-hidden="true" />
        </a>
      {:else}
        <span
          class="hero-badge provider-identity-chip"
          title={providerIdentityTitle}
          aria-label={providerIdentityTitle}
        >
          <span class="provider-identity-label">{providerIdentityLabel}</span>
        </span>
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
        <ToggleButton variant="outline" size="sm" class="size-7 p-0 data-[state=on]:text-[#e06070]" bind:pressed={() => isFavorite, () => onFavoriteToggle?.()} disabled={!onFavoriteToggle} aria-label={isFavorite ? "Remove from favorites" : "Add to favorites"}
        >
          <Heart class="h-4 w-4" />
        </ToggleButton>

        {#if isNsfw}
          <span class="action-badge nsfw active" aria-label="NSFW">
            <Flame class="h-4 w-4" />
          </span>
        {/if}

        <ToggleButton variant="outline" size="sm" class="size-7 p-0 data-[state=on]:text-[#80b898]" bind:pressed={() => isOrganized, () => onOrganizedToggle?.()} disabled={!onOrganizedToggle} aria-label={isOrganized ? "Mark as unorganized" : "Mark as organized"}
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
    gap: 0.15rem;
  }



  .position-badges {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    flex-wrap: wrap;
  }

  :global(.hero-badge.wanted) {
    color: var(--color-text-accent, #c7c9cc);
    border-color: color-mix(in srgb, var(--color-text-accent, #c7c9cc) 45%, transparent);
    text-transform: uppercase;
    letter-spacing: 0.06em;
  }

  :global(.hero-badge) {
    display: inline-flex;
    align-items: center;
    min-height: 1.45rem;
    padding: 0.2rem 0.62rem;
    border: 1px solid rgba(199, 201, 204, 0.38);
    border-radius: var(--radius-xs);
    background:
      linear-gradient(135deg, rgba(199, 201, 204, 0.11), rgba(255, 255, 255, 0.03)),
      color-mix(in srgb, var(--color-surface-2) 82%, var(--color-accent-900) 18%);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.06),
      0 0 8px rgba(199, 201, 204, 0.08);
    color: var(--color-accent-100);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    line-height: 1;
    text-transform: uppercase;
    text-shadow: 0 0 6px rgba(199, 201, 204, 0.16);
  }

  .provider-identity-chip {
    gap: 0.35rem;
    min-width: 0;
    max-width: 100%;
    text-decoration: none;
    text-transform: none;
  }

  a.provider-identity-chip { transition: border-color 0.15s, box-shadow 0.15s, color 0.15s; }

  a.provider-identity-chip:hover,
  a.provider-identity-chip:focus-visible {
    border-color: color-mix(in srgb, var(--detail-accent) 68%, transparent);
    box-shadow: inset 0 1px 0 rgba(255, 255,255, 0.08);
    color: var(--detail-accent);
    outline: none;
  }

  .provider-identity-label {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  :global(.provider-identity-link-icon) { flex: 0 0 auto; }

  .action-row {
    display: flex;
    flex-wrap: wrap;
    align-items: flex-start;
    justify-content: space-between;
    gap: 0.5rem;
    width: 100%;
  }

  .action-badges {
    display: flex;
    align-items: center;
    gap: 0.35rem;
  }

  .action-group {
    display: flex;
    flex: 1 1 auto;
    flex-wrap: wrap;
    min-width: 0;
    align-items: center;
    justify-content: flex-end;
    gap: 0.625rem;
    margin-left: auto;
  }

  .action-badge {
    display: grid;
    place-items: center;
    width: 1.75rem;
    height: 1.75rem;
    padding: 0;
    border: 1px solid var(--detail-border);
    border-radius: var(--radius-xs, 4px);
    background: rgba(255, 255, 255, 0.04);
    color: var(--detail-text-disabled);
    cursor: pointer;
    transition: color 0.2s, border-color 0.2s, box-shadow 0.2s, transform 0.2s;
  }

  .action-badge.nsfw {
    cursor: default;
    color: #e06070;
    border-color: rgba(224, 96, 112, 0.5);
    box-shadow: 0 0 8px rgba(224, 96, 112, 0.15);
    user-select: none;
    -webkit-user-select: none;
    pointer-events: none;
  }




  @media (max-width: 480px) {
    .rating-row { grid-area: rating; }
    .position-badges {
      grid-area: badges;
      justify-self: stretch;
      justify-content: flex-start;
      width: 100%;
    }
    .action-row {
      grid-area: actions;
      align-items: center;
      gap: 0.35rem;
    }
    .action-badges { justify-self: start; }
    .action-group {
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: 0.5rem;
      width: auto;
    }
  }
</style>
