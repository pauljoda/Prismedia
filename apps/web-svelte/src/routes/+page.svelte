<script lang="ts">
  import { onMount } from "svelte";
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
    PlayCircle,
    History,
  } from "@lucide/svelte";
  import { buttonVariants, cn } from "@prismedia/ui-svelte";
  import { fetchEntities, type EntityCard } from "$lib/api/entities";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import LogoMark from "$lib/components/LogoMark.svelte";

  interface DashboardSection {
    kind: string;
    label: string;
    icon: typeof Film;
    href: string;
    items: EntityCard[];
    cards: EntityThumbnailCard[];
  }

  const SECTION_DEFS: Omit<DashboardSection, "items" | "cards">[] = [
    { kind: "video", label: "Videos", icon: Film, href: "/videos" },
    { kind: "movie", label: "Movies", icon: Clapperboard, href: "/movies" },
    { kind: "video-series", label: "Series", icon: FolderOpen, href: "/series" },
    { kind: "gallery", label: "Galleries", icon: Layers, href: "/galleries" },
    { kind: "book", label: "Books", icon: BookOpen, href: "/books" },
    { kind: "image", label: "Images", icon: ImageIcon, href: "/images" },
    { kind: "audio-library", label: "Audio", icon: Music, href: "/audio" },
    { kind: "person", label: "People", icon: Users, href: "/people" },
    { kind: "studio", label: "Studios", icon: Building2, href: "/studios" },
    { kind: "tag", label: "Tags", icon: Tag, href: "/tags" },
  ];

  // Cross-kind activity rows that lead the dashboard: pick up where you left off, then what you
  // most recently finished. Both lean on the server-side engagement status + recency ordering.
  const ACTIVITY_LIMIT = 20;

  const nsfw = useNsfw();

  let loading = $state(true);
  let sections: DashboardSection[] = $state([]);
  let continueCards: EntityThumbnailCard[] = $state([]);
  let recentCards: EntityThumbnailCard[] = $state([]);

  const populatedSections = $derived(sections.filter((s) => s.cards.length > 0));
  const hasAnyContent = $derived(
    populatedSections.length > 0 || continueCards.length > 0 || recentCards.length > 0,
  );

  let lastNsfwMode = $state(nsfw.mode);

  onMount(() => {
    void loadDashboard();
  });

  $effect(() => {
    if (nsfw.mode !== lastNsfwMode) {
      lastNsfwMode = nsfw.mode;
      void loadDashboard();
    }
  });

  function toCards(items: EntityCard[]): EntityThumbnailCard[] {
    return items.map((item) => entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id)));
  }

  async function loadDashboard() {
    loading = true;
    const hideNsfw = nsfw.mode === "off";

    const sectionsPromise = Promise.allSettled(
      SECTION_DEFS.map(async (def) => {
        const response = await fetchEntities({
          kind: def.kind,
          sort: "added",
          sortDir: "desc",
          hideNsfw,
          limit: 20,
        });
        return { def, items: response.items };
      }),
    );
    const continuePromise = fetchEntities({
      status: "in-progress",
      sort: "last-played",
      sortDir: "desc",
      hideNsfw,
      limit: ACTIVITY_LIMIT,
    })
      .then((r) => r.items)
      .catch(() => [] as EntityCard[]);
    const recentPromise = fetchEntities({
      status: "watched",
      sort: "last-played",
      sortDir: "desc",
      hideNsfw,
      limit: ACTIVITY_LIMIT,
    })
      .then((r) => r.items)
      .catch(() => [] as EntityCard[]);

    const [results, continueItems, recentItems] = await Promise.all([
      sectionsPromise,
      continuePromise,
      recentPromise,
    ]);

    sections = results.map((result, i) => {
      const def = SECTION_DEFS[i];
      if (result.status === "fulfilled") {
        const items = result.value.items.slice(0, 20);
        return { ...def, items, cards: toCards(items) };
      }
      return { ...def, items: [], cards: [] };
    });
    continueCards = toCards(continueItems);
    recentCards = toCards(recentItems);

    loading = false;
  }
</script>

{#snippet shelf(label: string, Icon: typeof Film, cards: EntityThumbnailCard[], href: string | null)}
  <section>
    <div class="flex items-center justify-between mb-4 px-3">
      <h2 class="text-lg font-semibold flex items-center gap-2">
        <Icon class="w-4.5 h-4.5 text-accent-500" />
        {label}
      </h2>
      {#if href}
        <a
          {href}
          class="inline-flex items-center gap-1 text-xs text-text-muted hover:text-text-accent transition-colors"
        >
          View all
          <ChevronRight class="h-3.5 w-3.5" />
        </a>
      {/if}
    </div>

    <div class="flex gap-3 overflow-x-auto pt-1 pb-5 snap-x snap-mandatory scrollbar-hidden px-3">
      {#each cards as card (card.entity.id)}
        <div class="flex-none snap-start" style:width="clamp(140px, 18vw, 220px)">
          <EntityThumbnail {card} />
        </div>
      {/each}
    </div>
  </section>
{/snippet}

<svelte:head>
  <title>Dashboard — Prismedia</title>
</svelte:head>

{#if loading}
  <div class="min-h-[80vh] flex items-center justify-center">
    <div class="flex flex-col items-center gap-6">
      <div class="relative flex items-center justify-center">
        <div class="route-loader-core-field"></div>
        <div class="route-loader-ripples">
          <div class="route-loader-ripple-ring"></div>
          <div class="route-loader-ripple-ring"></div>
          <div class="route-loader-ripple-ring"></div>
        </div>
        <LogoMark size={40} alt="" />
      </div>
      <p class="text-sm text-text-muted">Loading your library…</p>
    </div>
  </div>
{:else if !hasAnyContent}
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
      href="/settings"
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
    {#if continueCards.length > 0}
      {@render shelf("Continue Watching", PlayCircle, continueCards, null)}
    {/if}
    {#if recentCards.length > 0}
      {@render shelf("Recently Watched", History, recentCards, null)}
    {/if}
    {#each populatedSections as section (section.kind)}
      {@render shelf(section.label, section.icon, section.cards, section.href)}
    {/each}
  </div>
{/if}
