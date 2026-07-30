<script lang="ts">
  import { CheckCircle, ExternalLink, Flame, Heart, Pencil, PencilOff, Star } from "@lucide/svelte";
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

  let favoriteAnimating = $state(false);
  let organizedAnimating = $state(false);
  let ratingAnim = $state<"fill" | "clear" | null>(null);
  let ratingAnimCount = $state(0);

  const providerIdentityLabel = $derived(card.providerIdentity?.pluginId ?? "");
  const providerIdentityTitle = $derived.by(() => {
    const identity = card.providerIdentity;
    if (!identity) return "";
    return `Metadata and monitoring source: ${identity.pluginId}, ${identity.identityNamespace} ID ${identity.identityValue}`;
  });

  function handleFavoriteClick(event: MouseEvent) {
    if (!onFavoriteToggle) return;
    (event.currentTarget as HTMLElement).blur();
    favoriteAnimating = true;
    onFavoriteToggle();
    setTimeout(() => (favoriteAnimating = false), 400);
  }

  function handleOrganizedClick(event: MouseEvent) {
    if (!onOrganizedToggle) return;
    (event.currentTarget as HTMLElement).blur();
    organizedAnimating = true;
    onOrganizedToggle();
    setTimeout(() => (organizedAnimating = false), 400);
  }

  function handleRatingClick(event: MouseEvent, value: number) {
    if (!onRatingChange || ratingBusy || !card.rating) return;
    (event.currentTarget as HTMLElement).blur();
    const clearing = card.rating.value === value;
    const nextValue = clearing ? null : value;

    ratingAnim = clearing ? "clear" : "fill";
    ratingAnimCount = clearing ? card.rating.value : value;
    onRatingChange(nextValue);

    const duration = clearing ? 350 : 80 * value + 200;
    setTimeout(() => (ratingAnim = null), duration);
  }
</script>

{#if card.rating}
  <div class="rating-row" role="group" aria-label="Rating">
    {#each { length: card.rating.max } as _, i (i)}
      {@const value = i + 1}
      {@const filling = ratingAnim === "fill" && value <= ratingAnimCount}
      {@const clearing = ratingAnim === "clear" && value <= ratingAnimCount}
      <button
        type="button"
        class="rating-star"
        class:active={card.rating.value >= value}
        class:star-fill={filling}
        class:star-clear={clearing}
        style:animation-delay={filling ? `${(value - 1) * 70}ms` : "0ms"}
        disabled={ratingBusy || !onRatingChange}
        aria-label={`Rate ${value}`}
        onclick={(event: MouseEvent) => handleRatingClick(event, value)}
      >
        <Star class="h-5 w-5" />
      </button>
    {/each}
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
        <button
          type="button"
          class="action-badge favorite"
          class:active={isFavorite}
          class:animating={favoriteAnimating}
          disabled={!onFavoriteToggle}
          aria-label={isFavorite ? "Remove from favorites" : "Add to favorites"}
          onclick={(event: MouseEvent) => handleFavoriteClick(event)}
        >
          <Heart class="h-4 w-4" />
        </button>

        {#if isNsfw}
          <span class="action-badge nsfw active" aria-label="NSFW">
            <Flame class="h-4 w-4" />
          </span>
        {/if}

        <button
          type="button"
          class="action-badge organized"
          class:active={isOrganized}
          class:animating={organizedAnimating}
          disabled={!onOrganizedToggle}
          aria-label={isOrganized ? "Mark as unorganized" : "Mark as organized"}
          onclick={(event: MouseEvent) => handleOrganizedClick(event)}
        >
          <CheckCircle class="h-4 w-4" />
        </button>
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

  .rating-star {
    display: grid;
    height: 1.75rem;
    width: 1.75rem;
    place-items: center;
    padding: 0;
    border: none;
    background: transparent;
    color: var(--detail-text-disabled);
    cursor: pointer;
    transition: color 0.15s, filter 0.15s;
  }

  .rating-star.active { color: var(--detail-accent); }
  .rating-star:focus { outline: none; }
  .rating-star.star-fill { animation: star-roll-in 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275) backwards; }
  .rating-star.star-clear { animation: star-pop-out 0.3s ease-out; }
  .rating-star:disabled { cursor: default; opacity: 0.7; }

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
    gap: 0.35rem;
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

  .action-badge:focus { outline: none; }
  .action-badge:disabled { cursor: default; opacity: 0.5; }
  .action-badge.favorite.active {
    color: #e06070;
    border-color: rgba(224, 96, 112, 0.5);
    box-shadow: 0 0 10px rgba(224, 96, 112, 0.2);
  }
  .action-badge.favorite.animating,
  .action-badge.organized.animating {
    animation: badge-pop 0.35s cubic-bezier(0.175, 0.885, 0.32, 1.275);
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
  .action-badge.organized.active {
    color: #80b898;
    border-color: rgba(78, 138, 98, 0.5);
    box-shadow: 0 0 10px rgba(78, 138, 98, 0.2);
  }

  @keyframes badge-pop {
    0% { transform: scale(1); }
    40% { transform: scale(1.3); }
    100% { transform: scale(1); }
  }

  @keyframes star-roll-in {
    0% { transform: scale(0) rotate(-90deg); opacity: 0; }
    60% { transform: scale(1.25) rotate(10deg); opacity: 1; }
    100% { transform: scale(1) rotate(0deg); opacity: 1; }
  }

  @keyframes star-pop-out {
    0% { transform: scale(1); }
    35% { transform: scale(1.35); }
    100% { transform: scale(1); }
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
      gap: 0.25rem;
      width: auto;
    }
  }
</style>
