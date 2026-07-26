<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import type { PlaybackStatisticsEvent } from "$lib/api/generated/model";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import {
    PLAYBACK_EVENT_KIND,
    labelForEntityKind,
    resolveEntityHref,
  } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import {
    formatDayLong,
    formatWatchDuration,
    localDayKey,
    statNumber,
  } from "$lib/stats/playback-stats";

  interface Props {
    events: PlaybackStatisticsEvent[];
    thumbnailFor: (event: PlaybackStatisticsEvent) => EntityThumbnailCard;
    /** Offset used to group events into the viewer's calendar days. */
    utcOffsetMinutes: number;
    class?: string;
  }

  let { events, thumbnailFor, utcOffsetMinutes, class: className }: Props = $props();

  interface EventGroup {
    dayKey: string;
    label: string;
    events: PlaybackStatisticsEvent[];
  }

  const groups = $derived.by<EventGroup[]>(() => {
    const ordered: EventGroup[] = [];
    for (const event of events) {
      const dayKey = localDayKey(event.occurredAt, utcOffsetMinutes);
      const current = ordered.at(-1);
      if (current?.dayKey === dayKey) {
        current.events.push(event);
        continue;
      }
      ordered.push({ dayKey, label: formatDayLong(dayKey), events: [event] });
    }
    return ordered;
  });

  const timeFormatter = new Intl.DateTimeFormat(undefined, {
    hour: "numeric",
    minute: "2-digit",
  });

  /** Squared so the title column stays aligned across mixed poster, square, and wide artwork. */
  function squared(card: EntityThumbnailCard): EntityThumbnailCard {
    return { ...card, aspectRatio: "square" };
  }

  /** How far into the item playback reached, or null when the item has no known duration. */
  function progressRatio(event: PlaybackStatisticsEvent): number | null {
    const duration = statNumber(event.durationSeconds);
    if (duration <= 0) return null;
    return Math.min(1, Math.max(0, statNumber(event.positionSeconds) / duration));
  }
</script>

<div class={cn("event-timeline", className)}>
  {#each groups as group (group.dayKey)}
    <section class="event-day">
      <h3 class="event-day-label">{group.label}</h3>
      <ol class="event-list">
        {#each group.events as event (event.id)}
          {@const skipped = event.kind === PLAYBACK_EVENT_KIND.skipped}
          {@const accent = entityAccentForKind(event.entityKind)}
          {@const href = resolveEntityHref(event.entityKind, event.entityId)}
          {@const card = thumbnailFor(event)}
          {@const ratio = progressRatio(event)}
          <li>
            <svelte:element
              this={href ? "a" : "div"}
              href={href ?? undefined}
              class={cn("event-row", href && "event-row-link")}
              style:--band-primary={accent.primary}
            >
              <span class="event-time">{timeFormatter.format(new Date(event.occurredAt))}</span>
              <span class={cn("event-node", skipped && "event-node-skipped")} aria-hidden="true"></span>

              <span class="event-thumb">
                <EntityThumbnail card={squared(card)} imageLoading="lazy" interactive={false} mediaOnly />
              </span>

              <span class="event-main">
                <span class="event-title">{event.entityTitle}</span>
                <span class="event-meta">
                  <span class={cn("event-state", skipped && "event-state-skipped")}>
                    {skipped ? "Skipped" : "Played"}
                  </span>
                  · {labelForEntityKind(event.entityKind)}
                  {#if ratio != null}
                    · {formatWatchDuration(statNumber(event.positionSeconds))} of {formatWatchDuration(statNumber(event.durationSeconds))}
                  {/if}
                </span>
                {#if ratio != null}
                  <span class="event-progress" aria-hidden="true">
                    <span
                      class={cn("event-progress-fill", skipped && "event-progress-fill-skipped")}
                      style:width={`${Math.max(1.5, ratio * 100)}%`}
                    ></span>
                  </span>
                {/if}
              </span>
            </svelte:element>
          </li>
        {/each}
      </ol>
    </section>
  {/each}
</div>

<style>
  .event-timeline {
    display: flex;
    flex-direction: column;
  }

  .event-day-label {
    position: sticky;
    top: 0;
    z-index: 1;
    margin: 0;
    padding: 0.4rem 0.75rem 0.35rem;
    border-top: 1px solid var(--color-border-subtle);
    background: var(--color-surface-1);
    font-family: var(--font-mono);
    font-size: 0.62rem;
    font-weight: 600;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: var(--color-text-muted);
  }

  .event-day:first-child .event-day-label {
    border-top: none;
  }

  .event-list {
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .event-row {
    position: relative;
    display: grid;
    grid-template-columns: 3.1rem 0.5rem auto minmax(0, 1fr);
    align-items: center;
    gap: 0.6rem;
    padding: 0.35rem 0.75rem;
    text-decoration: none;
    transition: background var(--duration-fast, 120ms) var(--ease-default, ease);
  }

  .event-row-link:hover {
    background: var(--color-surface-2);
  }

  /* A continuous spine behind the nodes reads the list as one run of history. */
  .event-row::before {
    content: "";
    position: absolute;
    top: 0;
    bottom: 0;
    left: calc(0.75rem + 3.1rem + 0.6rem + 0.25rem);
    width: 1px;
    background: var(--color-border-subtle);
  }

  .event-time {
    font-family: var(--font-mono);
    font-size: 0.64rem;
    font-variant-numeric: tabular-nums;
    text-align: right;
    color: var(--color-text-disabled);
  }

  .event-node {
    position: relative;
    z-index: 1;
    justify-self: center;
    width: 0.42rem;
    height: 0.42rem;
    border-radius: 50%;
    background: var(--band-primary);
    box-shadow: 0 0 0 3px var(--color-surface-1);
  }

  .event-node-skipped {
    background: var(--color-surface-4);
    box-shadow:
      0 0 0 1px var(--band-primary),
      0 0 0 3px var(--color-surface-1);
  }

  .event-row-link:hover .event-node {
    box-shadow: 0 0 0 3px var(--color-surface-2);
  }

  .event-row-link:hover .event-node-skipped {
    box-shadow:
      0 0 0 1px var(--band-primary),
      0 0 0 3px var(--color-surface-2);
  }

  .event-thumb {
    width: 2.6rem;
    height: 2.6rem;
    flex: 0 0 auto;
  }

  .event-thumb :global(.entity-thumbnail) {
    width: 100%;
    height: 100%;
  }

  .event-main {
    display: flex;
    flex-direction: column;
    gap: 0.14rem;
    min-width: 0;
  }

  .event-title {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.8rem;
    color: var(--color-text-primary);
  }

  .event-meta {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-family: var(--font-mono);
    font-size: 0.6rem;
    color: var(--color-text-disabled);
  }

  .event-progress {
    display: block;
    /* A short meter reads as a progress marker; stretched across the row it reads as a rule. */
    width: min(100%, 11rem);
    height: 2px;
    margin-top: 0.08rem;
    border-radius: 1px;
    background: var(--color-surface-3);
    overflow: hidden;
  }

  .event-progress-fill {
    display: block;
    height: 100%;
    border-radius: 1px;
    background: var(--color-accent-600);
  }

  .event-progress-fill-skipped {
    background: var(--color-warning);
    opacity: 0.75;
  }

  .event-state {
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    color: var(--color-text-muted);
  }

  .event-state-skipped {
    color: var(--color-warning-text);
  }

  @media (max-width: 40rem) {
    .event-row {
      grid-template-columns: 2.6rem 0.5rem auto minmax(0, 1fr);
    }

    .event-row::before {
      left: calc(0.75rem + 2.6rem + 0.6rem + 0.25rem);
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .event-row {
      transition: none;
    }
  }
</style>
