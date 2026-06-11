<script lang="ts">
  import "../app.css";

  import { afterNavigate } from "$app/navigation";
  import { onMount, tick } from "svelte";
  import type { Snapshot } from "@sveltejs/kit";
  import { cn } from "@prismedia/ui-svelte";
  import Sidebar from "$lib/components/Sidebar.svelte";
  import CanvasHeader from "$lib/components/CanvasHeader.svelte";
  import MobileNav from "$lib/components/MobileNav.svelte";
  import CommandPalette from "$lib/components/CommandPalette.svelte";
  import AudioVidStackPlayer from "$lib/components/AudioVidStackPlayer.svelte";

  import { type NsfwMode, parseNsfwModeCookie } from "$lib/nsfw/cookie";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import { browser } from "$app/environment";
  import { provideAppChrome } from "$lib/stores/app-chrome.svelte";
  import { provideNavCustomization } from "$lib/stores/nav-customization.svelte";
  import { MAIN_SCROLL_TOP_EVENT } from "$lib/stores/main-scroll";
  import { providePageSnapshots, type AppPageSnapshot } from "$lib/stores/page-snapshots.svelte";
  import { provideSearch } from "$lib/stores/search.svelte";
  import { provideAudioPlayback } from "$lib/stores/audio-playback.svelte";
  import { fetchMusicPlayerState, saveMusicPlayerState } from "$lib/api/music-player-state";

  function readNsfwCookie(): NsfwMode | null {
    if (!browser) return null;
    const match = document.cookie.match(/(?:^|;\s*)prismedia-nsfw-mode=([^;]*)/);
    return match ? parseNsfwModeCookie(decodeURIComponent(match[1])) : null;
  }

  const defaultLayoutData: Required<
    Pick<
      App.PageData,
      "hasNsfwModeCookie" | "initialNsfwMode" | "lanAutoEnable" | "initialCollapsed"
    >
  > = {
    hasNsfwModeCookie: readNsfwCookie() !== null,
    initialNsfwMode: readNsfwCookie() ?? "off",
    lanAutoEnable: false,
    initialCollapsed: false,
  };

  let { data, children: pageContent } = $props();
  const layoutData = $derived({ ...defaultLayoutData, ...data });

  // Wire all context providers once at the root. The stores themselves
  // attach keyboard listeners (Cmd+K, ⌘⇧Z) via $effect.root on client boot.
  provideNsfw(() => ({
    initialMode: readNsfwCookie() ?? layoutData.initialNsfwMode,
    lanAutoEnable: layoutData.lanAutoEnable,
    hasExplicitMode: readNsfwCookie() !== null || layoutData.hasNsfwModeCookie,
  }));
  const chrome = provideAppChrome(() => layoutData.initialCollapsed);
  provideNavCustomization();
  provideSearch();
  const playback = provideAudioPlayback();
  let mainScroller = $state<HTMLElement | null>(null);
  let musicPlayerPersistenceReady = $state(false);
  let lastMusicPlayerSnapshot = "";

  function musicPlayerSnapshot(): string {
    return JSON.stringify({
      queueTrackIds: playback.queue.map((track) => track.id),
      order: playback.order,
      position: playback.position,
      playing: playback.playIntent,
      shuffle: playback.shuffle,
      repeat: playback.repeat,
      volume: playback.volume,
      muted: playback.muted,
      collapsed: playback.collapsed,
      collapsedSide: playback.collapsedSide,
      context: playback.context,
    });
  }

  function scrollMainToTop() {
    void tick().then(() => {
      mainScroller?.scrollTo({ top: 0, left: 0 });
    });
  }

  afterNavigate(({ from, to, type }) => {
    if (!from?.url || !to?.url || type === "popstate") return;
    if (from.url.pathname === to.url.pathname) return;
    scrollMainToTop();
  });

  onMount(() => {
    window.addEventListener(MAIN_SCROLL_TOP_EVENT, scrollMainToTop);
    const controller = new AbortController();
    void fetchMusicPlayerState(controller.signal)
      .then((state) => playback.restore(state))
      .catch(() => {})
      .finally(() => {
        lastMusicPlayerSnapshot = musicPlayerSnapshot();
        musicPlayerPersistenceReady = true;
      });

    return () => {
      controller.abort();
      window.removeEventListener(MAIN_SCROLL_TOP_EVENT, scrollMainToTop);
    };
  });

  $effect(() => {
    if (!musicPlayerPersistenceReady) return;
    const snapshot = musicPlayerSnapshot();
    if (snapshot === lastMusicPlayerSnapshot) return;

    const timeout = window.setTimeout(() => {
      lastMusicPlayerSnapshot = snapshot;
      void saveMusicPlayerState(JSON.parse(snapshot)).catch(() => {});
    }, 350);

    return () => window.clearTimeout(timeout);
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

  // The floating-player inset changes the scroll container's bottom padding, which
  // viewport-fitting components (e.g. EntityGrid) read on resize. A padding-only
  // change doesn't trip their ResizeObserver, so nudge a re-measure when it changes.
  $effect(() => {
    void chrome.bottomDockInsetPx;
    if (!browser) return;
    window.dispatchEvent(new Event("resize"));
  });
</script>

<div
  class="flex min-h-dvh"
  style:--prismedia-bottom-dock-padding={bottomDockPadding}
  style:--prismedia-mobile-nav-height="calc(3.65rem + max(1.25rem, env(safe-area-inset-bottom, 0px)))"
  style:--prismedia-mobile-bottom-clearance="calc(3.65rem + max(1.25rem, env(safe-area-inset-bottom, 0px)) + var(--prismedia-bottom-dock-padding))"
  style:--prismedia-desktop-bottom-clearance="var(--prismedia-bottom-dock-padding)"
>
  <!-- Desktop sidebar -->
  <div class="hidden md:block">
    <Sidebar collapsed={chrome.sidebarCollapsed} onToggle={() => chrome.toggleSidebar()} />
  </div>

  <main
    bind:this={mainScroller}
    class={cn(
      "flex flex-1 flex-col transition-[margin-left] duration-moderate",
      "h-[calc(100dvh-var(--prismedia-mobile-nav-height))] overflow-y-auto [scrollbar-gutter:stable] md:h-dvh",
      chrome.sidebarCollapsed ? "md:ml-14" : "md:ml-60",
    )}
    style:transition-timing-function="var(--ease-mechanical)"
    style:padding-bottom="var(--prismedia-bottom-dock-padding)"
  >
    <CanvasHeader />
    <div class="flex-1 p-5">
      {@render pageContent()}
    </div>
  </main>

  <MobileNav />
  <CommandPalette />
  <AudioVidStackPlayer />
</div>
