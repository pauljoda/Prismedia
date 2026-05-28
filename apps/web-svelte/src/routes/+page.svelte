<script lang="ts">
  import { onMount } from "svelte";
  import {
    Film,
    BookOpen,
    Layers,
    Image as ImageIcon,
    Music,
    FolderOpen,
    Users,
    Building2,
    Tag,
    ChevronRight,
  } from "@lucide/svelte";
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
    { kind: "video-series", label: "Series", icon: FolderOpen, href: "/series" },
    { kind: "gallery", label: "Galleries", icon: Layers, href: "/galleries" },
    { kind: "book", label: "Books", icon: BookOpen, href: "/books" },
    { kind: "image", label: "Images", icon: ImageIcon, href: "/images" },
    { kind: "audio-library", label: "Audio", icon: Music, href: "/audio" },
    { kind: "person", label: "People", icon: Users, href: "/people" },
    { kind: "studio", label: "Studios", icon: Building2, href: "/studios" },
    { kind: "tag", label: "Tags", icon: Tag, href: "/tags" },
  ];

  const nsfw = useNsfw();

  let loading = $state(true);
  let sections: DashboardSection[] = $state([]);

  const populatedSections = $derived(sections.filter((s) => s.cards.length > 0));
  const hasAnyContent = $derived(populatedSections.length > 0);

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

  async function loadDashboard() {
    loading = true;
    const hideNsfw = nsfw.mode === "off";

    const results = await Promise.allSettled(
      SECTION_DEFS.map(async (def) => {
        const response = await fetchEntities({ kind: def.kind, hideNsfw });
        return { def, items: response.items };
      }),
    );

    sections = results.map((result, i) => {
      const def = SECTION_DEFS[i];
      if (result.status === "fulfilled") {
        const items = result.value.items.slice(0, 20);
        return {
          ...def,
          items,
          cards: items.map((item) => entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id))),
        };
      }
      return { ...def, items: [], cards: [] };
    });

    loading = false;
  }
</script>

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
      class="bg-accent-500 hover:bg-accent-400 text-accent-950 px-6 py-2.5 font-semibold transition-all duration-normal hover:shadow-[0_0_16px_rgba(196,154,90,0.3)]"
    >
      Configure library
    </a>
  </div>
{:else}
  <div class="space-y-10 pb-16 -mx-3">
    {#each populatedSections as section (section.kind)}
      <section>
        <div class="flex items-center justify-between mb-4 px-3">
          <h2 class="text-lg font-semibold flex items-center gap-2">
            <section.icon class="w-4.5 h-4.5 text-accent-500" />
            {section.label}
          </h2>
          <a
            href={section.href}
            class="inline-flex items-center gap-1 text-xs text-text-muted hover:text-text-accent transition-colors"
          >
            View all
            <ChevronRight class="h-3.5 w-3.5" />
          </a>
        </div>

        <div class="flex gap-3 overflow-x-auto pt-1 pb-5 snap-x snap-mandatory scrollbar-hidden px-3">
          {#each section.cards as card (card.entity.id)}
            <div class="flex-none snap-start" style:width="clamp(140px, 18vw, 220px)">
              <EntityThumbnail {card} />
            </div>
          {/each}
        </div>
      </section>
    {/each}
  </div>
{/if}
