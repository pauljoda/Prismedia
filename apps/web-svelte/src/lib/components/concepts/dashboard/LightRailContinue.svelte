<script lang="ts">
  import { ChevronLeft, ChevronRight } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import {
    resolveEntityThumbnailHref,
    toAspectRatioNumeric,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-thumbnail";

  interface Props {
    cards: EntityThumbnailCard[];
  }

  let { cards }: Props = $props();

  const railId = $props.id();
  let scroller: HTMLDivElement | undefined;
  let canScrollBack = $state(false);
  let canScrollForward = $state(false);

  interface Rail {
    label: string | null;
    percent: number;
  }

  function toPercent(fraction: number | null | undefined): number {
    return typeof fraction === "number" ? Math.round(Math.min(1, Math.max(0, fraction)) * 100) : 0;
  }

  /** A Book read and listened separately keeps two rails; everything else has one. */
  function railsFor(card: EntityThumbnailCard): Rail[] {
    if (card.separateProgress) {
      return [
        { label: "Read", percent: toPercent(card.progress) },
        { label: "Listened", percent: toPercent(card.listeningProgress) },
      ];
    }
    return [{ label: null, percent: toPercent(card.progress) }];
  }

  /** The caption draws progress as a light rail, so the artwork carries no meter of its own. */
  function artworkOnly(card: EntityThumbnailCard): EntityThumbnailCard {
    return { ...card, progress: null, listeningProgress: null, separateProgress: false };
  }

  function observeScroll(node: HTMLDivElement) {
    scroller = node;
    const update = () => {
      canScrollBack = node.scrollLeft > 1;
      canScrollForward = node.scrollLeft + node.clientWidth < node.scrollWidth - 1;
    };
    const observer = new ResizeObserver(update);
    observer.observe(node);
    node.addEventListener("scroll", update, { passive: true });
    update();
    return () => {
      observer.disconnect();
      node.removeEventListener("scroll", update);
      scroller = undefined;
    };
  }

  function scrollPage(direction: number) {
    scroller?.scrollBy({
      left: direction * scroller.clientWidth * 0.8,
      behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth",
    });
  }
</script>

<section aria-labelledby="{railId}-title" class="flex flex-col gap-4">
  <div class="flex min-h-8 items-center justify-between gap-3 px-3">
    <h2 id="{railId}-title" class="font-mono text-[0.68rem] uppercase tracking-[0.2em] text-text-muted">
      Continue <span class="text-text-disabled">· {cards.length}</span>
    </h2>
    {#if canScrollBack || canScrollForward}
      <div class="flex gap-1">
        <Button variant="ghost" size="icon-sm" aria-label="Previous in Continue" aria-controls={railId} disabled={!canScrollBack} onclick={() => scrollPage(-1)}><ChevronLeft /></Button>
        <Button variant="ghost" size="icon-sm" aria-label="Next in Continue" aria-controls={railId} disabled={!canScrollForward} onclick={() => scrollPage(1)}><ChevronRight /></Button>
      </div>
    {/if}
  </div>

  <div id={railId} class="rail flex snap-x snap-proximity gap-5 overflow-x-auto px-3 pb-3" {@attach observeScroll}>
    {#each cards as card (`${card.entity.kind}:${card.entity.id}`)}
      {@const accent = entityAccentForKind(card.entity.kind)}
      {@const href = resolveEntityThumbnailHref(card)}
      <article class="item flex flex-none snap-start flex-col gap-3" style:--item-aspect={toAspectRatioNumeric(card.aspectRatio)}>
        <EntityThumbnail card={artworkOnly(card)} mediaOnly artworkReactive={false} showBadges={false} />
        <div class="flex min-w-0 flex-col gap-1.5">
          <a {href} class="truncate font-heading text-[0.95rem] font-medium text-text-primary hover:underline hover:decoration-[var(--color-border-accent)] hover:underline-offset-4">
            {card.entity.title}
          </a>
          {#if card.subtitle}
            <p class="truncate text-caption text-text-muted">{card.subtitle}</p>
          {/if}
          {#each railsFor(card) as rail (rail.label ?? "progress")}
            <div class="flex items-center gap-2.5" title={rail.label ? `${rail.label} ${rail.percent}%` : `${rail.percent}%`}>
              {#if rail.label}
                <span class="w-14 shrink-0 font-mono text-[0.6rem] uppercase tracking-[0.12em] text-text-disabled">{rail.label}</span>
              {/if}
              <span class="track" aria-hidden="true">
                <span class="light" style:width="{rail.percent}%" style:--rail-color={accent.primary}></span>
              </span>
              <span class="w-8 shrink-0 text-right font-mono text-[0.68rem] tabular-nums text-text-muted">{rail.percent}%</span>
            </div>
          {/each}
        </div>
      </article>
    {/each}
  </div>
</section>

<style>
  .rail {
    --rail-height: clamp(13rem, 26vw, 20rem);
    scrollbar-width: thin;
    scroll-padding-inline: 0.75rem;
  }

  /* Every card shares one artwork height, so mixed shapes line up; phones cap the widest frames. */
  .item {
    width: min(84vw, calc(var(--rail-height) * var(--item-aspect)));
    min-width: 9rem;
  }

  .track {
    position: relative;
    display: block;
    flex: 1 1 auto;
    height: 2px;
    border-radius: 1px;
    background: rgb(255 255 255 / 0.08);
    overflow: hidden;
  }

  /* White light that takes on the family's colour at its leading edge. */
  .light {
    display: block;
    height: 100%;
    background: linear-gradient(90deg, rgb(255 255 255 / 0.28), rgb(255 255 255 / 0.78) 72%, var(--rail-color));
  }
</style>
