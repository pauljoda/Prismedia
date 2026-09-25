<script lang="ts">
  import { BookOpen, Play } from "@lucide/svelte";
  import { buttonVariants, cn } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { ENTITY_ENGAGEMENT_MODE, ENTITY_KIND_DEFINITIONS } from "$lib/api/generated/codes";
  import { paletteFromImage, type ArtworkPalette } from "$lib/entities/artwork-palette";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { displayNameForEntityKind, isEntityKindCode } from "$lib/entities/entity-codes";
  import {
    resolveEntityThumbnailHref,
    toAspectRatioNumeric,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-thumbnail";

  interface Props {
    /** The most recently touched in-progress item. */
    card: EntityThumbnailCard;
    /** The next few in-progress items, listed quietly under the feature. */
    next?: EntityThumbnailCard[];
    class?: string;
  }

  let { card, next = [], class: className }: Props = $props();

  let palette = $state<{ entityId: string; value: ArtworkPalette } | null>(null);

  const accent = $derived(entityAccentForKind(card.entity.kind));
  /** Atmosphere comes from the artwork once it has decoded, and from the family pair until then. */
  const tones = $derived(
    palette?.entityId === card.entity.id
      ? palette.value
      : { primary: accent.primary, secondary: accent.secondary, background: "#0b0c0e" },
  );
  const href = $derived(resolveEntityThumbnailHref(card));
  const aspect = $derived(toAspectRatioNumeric(card.aspectRatio));
  const reads = $derived(
    isEntityKindCode(card.entity.kind) &&
      ENTITY_KIND_DEFINITIONS[card.entity.kind].engagementMode === ENTITY_ENGAGEMENT_MODE.reading,
  );
  /** The billboard draws progress itself, so the artwork carries no second meter. */
  const artCard = $derived<EntityThumbnailCard>({ ...card, progress: null, listeningProgress: null, separateProgress: false });

  interface ProgressLine {
    label: string;
    percent: number;
  }

  const progressLines = $derived.by<ProgressLine[]>(() => {
    const lines: ProgressLine[] = [];
    if (typeof card.progress === "number" && card.progress > 0) {
      lines.push({ label: card.separateProgress ? "Read" : "Progress", percent: toPercent(card.progress) });
    }
    if (card.separateProgress && typeof card.listeningProgress === "number" && card.listeningProgress > 0) {
      lines.push({ label: "Listened", percent: toPercent(card.listeningProgress) });
    }
    return lines;
  });

  function toPercent(fraction: number): number {
    return Math.round(Math.min(1, Math.max(0, fraction)) * 100);
  }

  function captureArtwork(image: HTMLImageElement) {
    const value = paletteFromImage(image);
    if (value) palette = { entityId: card.entity.id, value };
  }
</script>

<section
  aria-labelledby="billboard-title"
  class={cn("billboard relative isolate flex flex-col overflow-hidden rounded-[var(--radius-xl)] border border-[var(--color-border-subtle)]", className)}
  style:--art-primary={tones.primary}
  style:--art-secondary={tones.secondary}
  style:--art-background={tones.background}
>
  <div class="atmosphere" aria-hidden="true"></div>

  <div class="flex flex-1 flex-col justify-end gap-5 p-5 sm:flex-row sm:items-end sm:gap-7 sm:p-7">
    <div class="art-frame shrink-0" style:--art-aspect={aspect}>
      <EntityThumbnail
        card={artCard}
        mediaOnly
        showBadges={false}
        hoverPreviewsEnabled={false}
        artworkReactive={false}
        imageLoading="eager"
        imageFetchPriority="high"
        onArtworkLoad={captureArtwork}
      />
    </div>

    <div class="flex min-w-0 flex-1 flex-col gap-3">
      <p class="font-mono text-[0.68rem] uppercase tracking-[0.2em] text-text-accent">
        Continue <span class="text-text-muted">· {displayNameForEntityKind(card.entity.kind)}</span>
      </p>
      <h1 id="billboard-title" class="font-heading text-2xl font-semibold leading-tight tracking-tight text-text-primary sm:text-4xl">
        <a {href} class="hover:underline hover:decoration-[var(--color-border-accent)] hover:underline-offset-4">{card.entity.title}</a>
      </h1>
      {#if card.subtitle}
        <p class="text-sm text-text-secondary">{card.subtitle}</p>
      {/if}

      {#each progressLines as line (line.label)}
        <div class="flex max-w-sm items-center gap-3">
          <span class="w-16 font-mono text-[0.68rem] uppercase tracking-[0.12em] text-text-muted">{line.label}</span>
          <div class="h-1 flex-1 overflow-hidden rounded-[2px] bg-white/10">
            <div
              class="h-full rounded-[2px]"
              style:width="{line.percent}%"
              style:background="linear-gradient(90deg, {accent.primary}, {accent.secondary})"
            ></div>
          </div>
          <span class="w-9 text-right font-mono text-[0.72rem] tabular-nums text-text-secondary">{line.percent}%</span>
        </div>
      {/each}

      <div class="mt-1 flex flex-wrap items-center gap-2">
        <a {href} class={cn(buttonVariants({ variant: "primary", size: "lg" }), "gap-2 px-5")}>
          {#if reads}
            <BookOpen class="h-4 w-4" aria-hidden="true" />
            Keep reading
          {:else}
            <Play class="h-4 w-4" aria-hidden="true" />
            Resume
          {/if}
        </a>
      </div>
    </div>
  </div>

  {#if next.length > 0}
    <div class="next-strip flex flex-col gap-3 border-t border-white/[0.06] px-5 py-4 sm:px-7">
      <p class="font-mono text-[0.64rem] uppercase tracking-[0.16em] text-text-muted">Also in progress</p>
      <ul class="next-row flex gap-3 overflow-x-auto pb-1">
        {#each next as item (`${item.entity.kind}:${item.entity.id}`)}
          <li class="next-item flex flex-none flex-col gap-1.5" style:--item-aspect={toAspectRatioNumeric(item.aspectRatio)}>
            <EntityThumbnail card={item} mediaOnly showBadges={false} hoverPreviewsEnabled={false} artworkReactive={false} />
            <a
              href={resolveEntityThumbnailHref(item)}
              class="truncate text-caption text-text-secondary transition-colors hover:text-text-primary"
              title={item.entity.title}
            >
              {item.entity.title}
            </a>
          </li>
        {/each}
      </ul>
    </div>
  {/if}
</section>

<style>
  .billboard {
    min-height: clamp(20rem, 42vh, 27rem);
  }

  /* Artwork drives a restrained atmosphere behind neutral text; it is never enlarged into a hero. */
  .atmosphere {
    position: absolute;
    inset: 0;
    z-index: -1;
    background:
      radial-gradient(60% 75% at 14% 72%, color-mix(in oklab, var(--art-secondary) 30%, transparent), transparent 68%),
      radial-gradient(75% 90% at 92% 8%, color-mix(in oklab, var(--art-primary) 26%, transparent), transparent 66%),
      linear-gradient(180deg, color-mix(in oklab, var(--art-background) 70%, #000) 0%, #050506 100%);
  }

  .art-frame {
    --art-height: 11rem;
    width: min(100%, calc(var(--art-height) * var(--art-aspect)));
  }

  @media (min-width: 640px) {
    .art-frame {
      --art-height: clamp(11rem, 20vw, 19rem);
    }
  }

  .next-strip {
    background: rgb(0 0 0 / 0.28);
  }

  .next-row {
    --next-height: 6.5rem;
    scrollbar-width: thin;
  }

  @media (min-width: 640px) {
    .next-row {
      --next-height: 7.25rem;
    }
  }

  /* Every item shares one artwork height, so posters, squares, and frames line up in one row. */
  .next-item {
    width: calc(var(--next-height) * var(--item-aspect));
    min-width: 5.5rem;
  }
</style>
