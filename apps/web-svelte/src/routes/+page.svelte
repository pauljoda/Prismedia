<script lang="ts">
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import { onDestroy, onMount } from "svelte";
  import {
    Film,
    BookOpen,
    Clapperboard,
    Layers,
    Image as ImageIcon,
    Music,
    FolderOpen,
    Users,
    Building2,
    Tag,
    ChevronRight,
    Play,
    PlayCircle,
    History,
  } from "@lucide/svelte";
  import { buttonVariants, cn } from "@prismedia/ui-svelte";
  import { fetchEntities, type EntityCard } from "$lib/api/entities";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { iconForKind, type EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import EntityShelf from "$lib/components/entities/EntityShelf.svelte";
  import LogoMark from "$lib/components/LogoMark.svelte";
  import PrismediaLoadingMark from "$lib/components/PrismediaLoadingMark.svelte";

  type SectionStatus = "pending" | "loading" | "ready";
  type SectionDisplay = "shelf" | "chips";

  interface SectionDef {
    kind: string;
    label: string;
    icon: typeof Film;
    href: string;
    display: SectionDisplay;
  }

  interface DashboardSection extends SectionDef {
    status: SectionStatus;
    cards: EntityThumbnailCard[];
  }

  const SECTION_DEFS: SectionDef[] = [
    { kind: "video", label: "Videos", icon: Film, href: "/videos", display: "shelf" },
    { kind: "movie", label: "Movies", icon: Clapperboard, href: "/movies", display: "shelf" },
    { kind: ENTITY_KIND.videoSeries, label: "Series", icon: FolderOpen, href: "/series", display: "shelf" },
    { kind: "gallery", label: "Galleries", icon: Layers, href: "/galleries", display: "shelf" },
    { kind: "book", label: "Books", icon: BookOpen, href: "/books", display: "shelf" },
    { kind: "image", label: "Images", icon: ImageIcon, href: "/images", display: "shelf" },
    { kind: ENTITY_KIND.audioLibrary, label: "Audio", icon: Music, href: "/audio", display: "shelf" },
    { kind: "person", label: "People", icon: Users, href: "/people", display: "shelf" },
    { kind: "studio", label: "Studios", icon: Building2, href: "/studios", display: "shelf" },
    { kind: "tag", label: "Tags", icon: Tag, href: "/tags", display: "chips" },
  ];

  // Cross-kind activity rows that lead the dashboard: pick up where you left off, then what you
  // most recently finished. Both lean on the server-side engagement status + recency ordering.
  const ACTIVITY_LIMIT = 20;
  const SECTION_LIMIT = 20;

  const nsfw = useNsfw();

  let activityReady = $state(false);
  let sections: DashboardSection[] = $state(
    SECTION_DEFS.map((def) => ({ ...def, status: "pending", cards: [] })),
  );
  let continueCards: EntityThumbnailCard[] = $state([]);
  let recentCards: EntityThumbnailCard[] = $state([]);

  // The billboard always features the single most recent in-progress item so it
  // never disagrees with the Continue row beneath it (which carries on from the
  // second item). Cards without artwork get a material fallback backdrop.
  const heroCard = $derived(continueCards[0] ?? null);
  const continueRowCards = $derived(continueCards.slice(1));

  /** Lucide icon for the hero fallback backdrop, derived from the entity kind. */
  const heroFallbackIcon = $derived.by(() => {
    if (!heroCard) return PlayCircle;
    switch (iconForKind(heroCard.entity.kind)) {
      case "audio":
        return Music;
      case "book":
        return BookOpen;
      case "gallery":
        return Layers;
      case "image":
        return ImageIcon;
      case "person":
        return Users;
      case "studio":
        return Building2;
      case "tag":
        return Tag;
      case "collection":
        return FolderOpen;
      default:
        return Film;
    }
  });

  const allSectionsSettled = $derived(sections.every((s) => s.status === "ready"));
  const hasAnyContent = $derived(
    continueCards.length > 0 ||
      recentCards.length > 0 ||
      sections.some((s) => s.cards.length > 0),
  );
  const showEmptySplash = $derived(activityReady && allSectionsSettled && !hasAnyContent);

  let lastNsfwMode = $state(nsfw.mode);
  let sectionObserver: IntersectionObserver | null = null;

  onMount(() => {
    void loadActivity();
  });

  onDestroy(() => {
    sectionObserver?.disconnect();
    sectionObserver = null;
  });

  $effect(() => {
    if (nsfw.mode !== lastNsfwMode) {
      lastNsfwMode = nsfw.mode;
      void loadActivity();
      for (const section of sections) {
        if (section.status !== "pending") void loadSection(section.kind);
      }
    }
  });

  function toCards(items: EntityCard[]): EntityThumbnailCard[] {
    return items.map((item) => entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id)));
  }

  async function loadActivity() {
    const hideNsfw = nsfw.mode === "off";
    const [continueItems, recentItems] = await Promise.all([
      fetchEntities({
        status: "in-progress",
        sort: "last-played",
        sortDir: "desc",
        hideNsfw,
        limit: ACTIVITY_LIMIT,
      })
        .then((r) => r.items)
        .catch(() => [] as EntityCard[]),
      fetchEntities({
        status: "watched",
        sort: "last-played",
        sortDir: "desc",
        hideNsfw,
        limit: ACTIVITY_LIMIT,
      })
        .then((r) => r.items)
        .catch(() => [] as EntityCard[]),
    ]);

    continueCards = toCards(continueItems);
    recentCards = toCards(recentItems);
    activityReady = true;

    // A library with no engagement yet is small or brand new: resolve every
    // section immediately so the empty-library splash can render decisively
    // instead of waiting on scroll-triggered loads that may never fire.
    if (continueItems.length === 0 && recentItems.length === 0) {
      for (const def of SECTION_DEFS) void loadSection(def.kind);
    }
  }

  async function loadSection(kind: string) {
    const index = sections.findIndex((s) => s.kind === kind);
    if (index < 0) return;
    sections[index] = { ...sections[index], status: "loading" };

    try {
      const response = await fetchEntities({
        kind,
        sort: "added",
        sortDir: "desc",
        hideNsfw: nsfw.mode === "off",
        limit: SECTION_LIMIT,
      });
      sections[index] = { ...sections[index], status: "ready", cards: toCards(response.items) };
    } catch {
      sections[index] = { ...sections[index], status: "ready", cards: [] };
    }
  }

  /**
   * Defers a section's fetch until its shell approaches the viewport, so the
   * dashboard's first paint only pays for the rows the user can actually see.
   */
  function lazySection(node: HTMLElement, kind: string) {
    sectionObserver ??= new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) continue;
          sectionObserver?.unobserve(entry.target);
          const targetKind = (entry.target as HTMLElement).dataset.sectionKind;
          if (!targetKind) continue;
          const section = sections.find((s) => s.kind === targetKind);
          if (section?.status === "pending") void loadSection(targetKind);
        }
      },
      { rootMargin: "800px 0px" },
    );
    node.dataset.sectionKind = kind;
    sectionObserver.observe(node);
    return {
      destroy() {
        sectionObserver?.unobserve(node);
      },
    };
  }

  function heroProgressPercent(card: EntityThumbnailCard): number {
    const value = typeof card.progress === "number" ? card.progress : 0;
    return Math.round(Math.min(1, Math.max(0, value)) * 100);
  }
</script>

{#snippet shelfSkeleton(label: string, Icon: typeof Film)}
  <section aria-hidden="true">
    <div class="flex items-center justify-between mb-4 px-3">
      <h2 class="text-lg font-semibold flex items-center gap-2 text-text-disabled">
        <Icon class="w-4.5 h-4.5" />
        {label}
      </h2>
    </div>
    <div class="flex gap-3 overflow-hidden pt-1 pb-5 px-3">
      {#each Array(7) as _, i (i)}
        <div class="flex-none" style:width="clamp(140px, 18vw, 220px)">
          <div class="aspect-video rounded-lg bg-surface-2 animate-pulse"></div>
          <div class="mt-2 h-3 w-3/4 rounded-sm bg-surface-2 animate-pulse"></div>
        </div>
      {/each}
    </div>
  </section>
{/snippet}

{#snippet chipBand(label: string, Icon: typeof Film, cards: EntityThumbnailCard[], href: string)}
  <section>
    <div class="flex items-center justify-between mb-3 px-3">
      <h2 class="text-lg font-semibold flex items-center gap-2">
        <Icon class="w-4.5 h-4.5 text-accent-500" />
        {label}
      </h2>
      <a
        {href}
        class="inline-flex items-center gap-1 text-xs text-text-muted hover:text-text-accent transition-colors"
      >
        View all
        <ChevronRight class="h-3.5 w-3.5" />
      </a>
    </div>
    <div class="flex flex-wrap gap-2 px-3 pb-2">
      {#each cards as card (card.entity.id)}
        <a
          href={card.href}
          class="inline-flex items-center gap-1.5 rounded-md border border-border-default bg-surface-2 px-3 py-1.5 text-sm text-text-secondary transition-colors hover:border-border-accent hover:text-text-accent"
        >
          <Icon class="h-3.5 w-3.5 opacity-60" />
          {card.entity.title}
        </a>
      {/each}
    </div>
  </section>
{/snippet}

<svelte:head>
  <title>Dashboard — Prismedia</title>
</svelte:head>

{#if !activityReady}
  <div class="min-h-[80vh] grid place-items-center">
    <PrismediaLoadingMark label="Loading your library" showLabel />
  </div>
{:else if showEmptySplash}
  <div class="min-h-[80vh] flex flex-col items-center justify-center text-center p-8">
    <div class="mb-8 opacity-80">
      <LogoMark size={96} alt="Prismedia — empty library" />
    </div>
    <h1 class="text-3xl font-bold text-text-primary mb-4">Your library is empty</h1>
    <p class="text-text-muted max-w-md mb-8">
      It looks like you haven't added any media yet. Head over to Settings to configure
      your library roots and start scanning.
    </p>
    <a
      href="/settings/libraries"
      class={cn(
        buttonVariants({ variant: "primary", size: "lg" }),
        "min-h-11 px-6 text-base font-semibold",
      )}
    >
      Configure library
    </a>
  </div>
{:else}
  <div class="space-y-10 pb-16 -mx-3">
    {#if heroCard}
      <section class="px-3">
        <a
          href={heroCard.href}
          class="group relative block overflow-hidden rounded-xl border border-border-subtle"
          style:height="clamp(280px, 38vh, 420px)"
        >
          {#if heroCard.cover?.src}
            <!-- The artwork fills the entire banner; scrims keep the text legible.
                 This is the page's LCP image, so it loads eagerly at high priority. -->
            <img
              src={heroCard.cover.src}
              alt=""
              decoding="async"
              fetchpriority="high"
              class="absolute inset-0 h-full w-full object-cover"
            />
            <div
              class="absolute inset-0"
              style:background="linear-gradient(90deg, rgba(7,8,11,0.88) 0%, rgba(7,8,11,0.55) 35%, rgba(7,8,11,0.05) 70%)"
            ></div>
          {:else}
            <!-- No artwork: material gradient backdrop with a dim kind mark. -->
            {@const HeroIcon = heroFallbackIcon}
            <div
              class="absolute inset-0"
              style:background="linear-gradient(135deg, #202734 0%, #11161d 45%, #07080b 100%)"
            ></div>
            <HeroIcon
              aria-hidden="true"
              class="absolute right-[8%] top-1/2 hidden h-44 w-44 -translate-y-1/2 text-text-disabled opacity-25 md:block"
            />
          {/if}
          <div
            class="absolute inset-0"
            style:background="linear-gradient(180deg, rgba(7,8,11,0.25) 0%, rgba(7,8,11,0.55) 60%, rgba(7,8,11,0.92) 100%)"
          ></div>

          <div class="relative z-10 flex h-full max-w-2xl flex-col justify-end gap-3 p-5 sm:p-8">
            <p class="font-mono text-[11px] uppercase tracking-[0.25em] text-text-accent">
              Continue
            </p>
            <h1 class="text-2xl font-semibold leading-tight text-text-primary sm:text-4xl">
              {heroCard.entity.title}
            </h1>
            {#if heroCard.subtitle}
              <p class="text-sm text-text-muted">{heroCard.subtitle}</p>
            {/if}

            {#if typeof heroCard.progress === "number" && heroCard.progress > 0}
              <div class="flex items-center gap-3">
                <div class="h-1 w-full max-w-xs overflow-hidden rounded-xs bg-white/10">
                  <div
                    class="h-full rounded-xs"
                    style:width="{heroProgressPercent(heroCard)}%"
                    style:background="linear-gradient(90deg, #397f70 0 34%, #a1833b 34% 68%, #a24952 68% 100%)"
                  ></div>
                </div>
                <span class="font-mono text-[11px] text-text-muted">
                  {heroProgressPercent(heroCard)}%
                </span>
              </div>
            {/if}

            <div class="mt-1 flex items-center gap-3">
              <span
                class={cn(
                  buttonVariants({ variant: "primary", size: "md" }),
                  "pointer-events-none gap-2 px-5",
                )}
              >
                <Play class="h-4 w-4" />
                Resume
              </span>
            </div>
          </div>
        </a>
      </section>
    {/if}

    {#if continueRowCards.length > 0}
      <EntityShelf label="Continue" icon={PlayCircle} cards={continueRowCards} sizing="height" />
    {/if}
    {#if recentCards.length > 0}
      <EntityShelf label="Recent" icon={History} cards={recentCards} sizing="height" />
    {/if}

    {#each sections as section (section.kind)}
      {#if section.status === "ready" && section.cards.length === 0}
        <!-- Loaded and empty: contribute nothing. -->
      {:else if section.status !== "ready"}
        <div use:lazySection={section.kind}>
          {@render shelfSkeleton(section.label, section.icon)}
        </div>
      {:else if section.display === "chips"}
        {@render chipBand(section.label, section.icon, section.cards, section.href)}
      {:else}
        <EntityShelf
          label={section.label}
          icon={section.icon}
          cards={section.cards}
          href={section.href}
        />
      {/if}
    {/each}
  </div>
{/if}
