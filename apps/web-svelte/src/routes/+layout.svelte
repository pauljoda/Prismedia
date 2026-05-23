<script lang="ts">
  import "../app.css";

  import { afterNavigate } from "$app/navigation";
  import { tick } from "svelte";
  import type { Snapshot } from "@sveltejs/kit";
  import { cn } from "@prismedia/ui-svelte";
  import Sidebar from "$lib/components/Sidebar.svelte";
  import CanvasHeader from "$lib/components/CanvasHeader.svelte";
  import MobileNav from "$lib/components/MobileNav.svelte";
  import CommandPalette from "$lib/components/CommandPalette.svelte";
  import PlaylistController from "$lib/components/PlaylistController.svelte";

  import { type NsfwMode, parseNsfwModeCookie } from "$lib/nsfw/cookie";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import { browser } from "$app/environment";
  import { provideAppChrome } from "$lib/stores/app-chrome.svelte";
  import { providePageSnapshots, type AppPageSnapshot } from "$lib/stores/page-snapshots.svelte";
  import { provideSearch } from "$lib/stores/search.svelte";
  import { providePlaylist } from "$lib/stores/playlist.svelte";

  function readNsfwCookie(): NsfwMode {
    if (!browser) return "off";
    const match = document.cookie.match(/(?:^|;\s*)prismedia-nsfw-mode=([^;]*)/);
    return parseNsfwModeCookie(match ? decodeURIComponent(match[1]) : undefined);
  }

  const defaultLayoutData: Required<
    Pick<App.PageData, "initialNsfwMode" | "lanAutoEnable" | "initialCollapsed">
  > = {
    initialNsfwMode: readNsfwCookie(),
    lanAutoEnable: false,
    initialCollapsed: false,
  };

  let { data, children: pageContent } = $props();
  const layoutData = $derived({ ...defaultLayoutData, ...data });

  // Wire all context providers once at the root. The stores themselves
  // attach keyboard listeners (Cmd+K, ⌘⇧Z) via $effect.root on client boot.
  provideNsfw(() => ({
    initialMode: layoutData.initialNsfwMode,
    lanAutoEnable: layoutData.lanAutoEnable,
  }));
  const chrome = provideAppChrome(() => layoutData.initialCollapsed);
  provideSearch();
  const playlist = providePlaylist();
  let mainScroller = $state<HTMLElement | null>(null);

  $effect(() => {
    void playlist.hydrate();
  });

  afterNavigate(({ from, to, type }) => {
    if (!from?.url || !to?.url || type === "popstate") return;
    if (from.url.pathname === to.url.pathname) return;
    mainScroller?.scrollTo({ top: 0, left: 0 });
  });

  function restoreMainScroller(snapshot: { top: number; left: number }) {
    void tick().then(() => {
      let tries = 0;
      const run = () => {
        const scroller = mainScroller;
        if (!scroller) return;
        scroller.scrollTo({ top: snapshot.top, left: snapshot.left });
        tries += 1;
        const canReachTarget =
          snapshot.top <= 0 ||
          scroller.scrollTop >= snapshot.top ||
          scroller.scrollHeight - scroller.clientHeight >= snapshot.top;
        if (!canReachTarget && tries < 20) requestAnimationFrame(run);
      };
      requestAnimationFrame(run);
    });
  }

  const pageSnapshots = providePageSnapshots({
    captureScroll: () => ({
      top: mainScroller?.scrollTop ?? 0,
      left: mainScroller?.scrollLeft ?? 0,
    }),
    restoreScroll: restoreMainScroller,
  });

  export const snapshot: Snapshot<AppPageSnapshot> = {
    capture: () => pageSnapshots.capture(),
    restore: (saved) => pageSnapshots.restore(saved),
  };

  const bottomDockPadding = $derived(
    chrome.bottomDockInsetPx > 0 ? `${chrome.bottomDockInsetPx + 16}px` : "0px",
  );
  const playlistOffset = $derived(playlist.isActive ? "3.5rem" : "0px");
</script>

<div
  class="flex min-h-dvh"
  style:--prismedia-bottom-dock-padding={bottomDockPadding}
  style:--prismedia-playlist-offset={playlistOffset}
  style:--prismedia-mobile-bottom-clearance="calc(3.5rem + var(--prismedia-playlist-offset) + var(--prismedia-bottom-dock-padding))"
  style:--prismedia-desktop-bottom-clearance="calc(var(--prismedia-playlist-offset) + var(--prismedia-bottom-dock-padding))"
>
  <!-- Desktop sidebar -->
  <div class="hidden md:block">
    <Sidebar collapsed={chrome.sidebarCollapsed} onToggle={() => chrome.toggleSidebar()} />
  </div>

  <main
    bind:this={mainScroller}
    class={cn(
      "flex flex-1 flex-col transition-[margin-left] duration-moderate",
      "h-[calc(100dvh-var(--prismedia-mobile-bottom-clearance))] overflow-y-auto [scrollbar-gutter:stable] md:h-[calc(100dvh-var(--prismedia-desktop-bottom-clearance))]",
      chrome.sidebarCollapsed ? "md:ml-14" : "md:ml-60",
    )}
    style:transition-timing-function="var(--ease-mechanical)"
  >
    <CanvasHeader />
    <div class="flex-1 p-5">
      {@render pageContent()}
    </div>
  </main>

  <MobileNav />
  <CommandPalette />
  <PlaylistController />
</div>
