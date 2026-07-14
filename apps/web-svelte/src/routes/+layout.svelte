<script lang="ts">
  import "../app.css";

  import { afterNavigate, beforeNavigate, goto } from "$app/navigation";
  import { page } from "$app/state";
  import { onMount, tick } from "svelte";
  import type { Snapshot } from "@sveltejs/kit";
  import { cn } from "@prismedia/ui-svelte";
  import Sidebar from "$lib/components/Sidebar.svelte";
  import CanvasHeader from "$lib/components/CanvasHeader.svelte";
  import MobileNav from "$lib/components/MobileNav.svelte";
  import CommandPalette from "$lib/components/CommandPalette.svelte";
  import AudioVidStackPlayer from "$lib/components/AudioVidStackPlayer.svelte";
  import { provideSession } from "$lib/stores/session.svelte";

  import { type NsfwMode, parseNsfwModeCookie } from "$lib/nsfw/cookie";
  import { provideNsfw } from "$lib/nsfw/store.svelte";
  import { browser } from "$app/environment";
  import { provideAppChrome } from "$lib/stores/app-chrome.svelte";
  import { provideNavCustomization } from "$lib/stores/nav-customization.svelte";
  import { MAIN_SCROLL_TOP_EVENT } from "$lib/stores/main-scroll";
  import { providePageSnapshots, type AppPageSnapshot } from "$lib/stores/page-snapshots.svelte";
  import { provideSearch } from "$lib/stores/search.svelte";
  import { AUDIO_PLAYBACK_SAVE_EVENT, provideAudioPlayback } from "$lib/stores/audio-playback.svelte";
  import {
    fetchMusicPlayerState,
    saveMusicPlayerState,
    type PersistMusicPlayerState,
  } from "$lib/api/music-player-state";

  function readNsfwCookie(): NsfwMode | null {
    if (!browser) return null;
    const match = document.cookie.match(/(?:^|;\s*)prismedia-nsfw-mode=([^;]*)/);
    return match ? parseNsfwModeCookie(decodeURIComponent(match[1])) : null;
  }

  const defaultLayoutData: Required<
    Pick<App.PageData, "initialNsfwMode" | "initialCollapsed">
  > = {
    initialNsfwMode: readNsfwCookie() ?? "off",
    initialCollapsed: false,
  };

  let { data, children: pageContent } = $props();
  const layoutData = $derived({ ...defaultLayoutData, ...data });

  // Wire all context providers once at the root. Session comes first so downstream
  // stores (NSFW cap, nav gating) can consume it. The stores themselves attach
  // keyboard listeners (Cmd+K, ⌘⇧Z) via $effect.root on client boot.
  // svelte-ignore state_referenced_locally -- the session seeds once per boot; auth
  // transitions always use full-page navigations, so live tracking is unnecessary.
  const session = provideSession({
    user: data.user ?? null,
    needsSetup: data.needsSetup ?? false,
  });

  // The login page and setup wizard render bare (no sidebar, header, nav, or player):
  // they are the app's front door and must never flash the authenticated shell.
  const bareShell = $derived(
    page.url.pathname === "/login" || page.url.pathname.startsWith("/setup"),
  );

  // Defense-in-depth for client-side navigations after boot: the root load guard only
  // runs on full loads, so mid-session transitions check the in-memory session.
  beforeNavigate((navigation) => {
    const path = navigation.to?.url.pathname;
    if (!path) return;
    if (session.status === "needs-setup" && !path.startsWith("/setup")) {
      navigation.cancel();
      void goto("/setup");
    } else if (session.status === "anonymous" && path !== "/login" && !path.startsWith("/setup")) {
      navigation.cancel();
      void goto(`/login?returnTo=${encodeURIComponent(path)}`);
    }
  });

  provideNsfw(() => ({
    initialMode: readNsfwCookie() ?? layoutData.initialNsfwMode ?? "off",
    allowed: session.allowNsfw,
  }));
  const chrome = provideAppChrome(() => layoutData.initialCollapsed);
  provideNavCustomization();
  provideSearch();
  const playback = provideAudioPlayback();
  let mainScroller = $state<HTMLElement | null>(null);
  let musicPlayerPersistenceReady = $state(false);
  let lastMusicPlayerSnapshot = "";
  let lastMusicPlayerTimeSnapshot = "";

  function musicPlayerState(): PersistMusicPlayerState {
    return {
      queueTrackIds: playback.queue.map((track) => track.id),
      order: playback.order,
      position: playback.position,
      currentTime: playback.currentTime,
      playing: playback.playIntent,
      shuffle: playback.shuffle,
      repeat: playback.repeat,
      volume: playback.volume,
      muted: playback.muted,
      collapsed: playback.collapsed,
      collapsedSide: playback.collapsedSide,
      context: playback.context,
    };
  }

  function musicPlayerSnapshotFromState(state: PersistMusicPlayerState): string {
    return JSON.stringify({
      queueTrackIds: state.queueTrackIds,
      order: state.order,
      position: state.position,
      playing: state.playing,
      shuffle: state.shuffle,
      repeat: state.repeat,
      volume: state.volume,
      muted: state.muted,
      collapsed: state.collapsed,
      collapsedSide: state.collapsedSide,
      context: state.context,
    });
  }

  function musicPlayerSnapshot(): string {
    return musicPlayerSnapshotFromState(musicPlayerState());
  }

  function musicPlayerTimeSnapshotFromState(state: PersistMusicPlayerState): string {
    return JSON.stringify({
      queueTrackIds: state.queueTrackIds,
      position: state.position,
      currentTime: Math.floor(state.currentTime),
      playing: state.playing,
    });
  }

  function persistMusicPlayerState() {
    if (!musicPlayerPersistenceReady) return;
    const state = musicPlayerState();
    lastMusicPlayerSnapshot = musicPlayerSnapshotFromState(state);
    lastMusicPlayerTimeSnapshot = musicPlayerTimeSnapshotFromState(state);
    void saveMusicPlayerState(state).catch(() => {});
  }

  function persistMusicPlayerTimeIfChanged() {
    if (!musicPlayerPersistenceReady || playback.queue.length === 0) return;
    const state = musicPlayerState();
    const snapshot = musicPlayerTimeSnapshotFromState(state);
    if (snapshot === lastMusicPlayerTimeSnapshot) return;
    persistMusicPlayerState();
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
    window.addEventListener(AUDIO_PLAYBACK_SAVE_EVENT, persistMusicPlayerState);
    window.addEventListener("pagehide", persistMusicPlayerState);
    const timePersistInterval = window.setInterval(persistMusicPlayerTimeIfChanged, 5000);
    const controller = new AbortController();
    // Music-player persistence needs a session; pre-login boots (login/setup) skip it.
    if (session.status === "authed") {
      void fetchMusicPlayerState(controller.signal)
        .then((state) => playback.restore(state))
        .catch(() => {})
        .finally(() => {
          const state = musicPlayerState();
          lastMusicPlayerSnapshot = musicPlayerSnapshotFromState(state);
          lastMusicPlayerTimeSnapshot = musicPlayerTimeSnapshotFromState(state);
          musicPlayerPersistenceReady = true;
        });
    }

    return () => {
      controller.abort();
      window.removeEventListener(MAIN_SCROLL_TOP_EVENT, scrollMainToTop);
      window.removeEventListener(AUDIO_PLAYBACK_SAVE_EVENT, persistMusicPlayerState);
      window.removeEventListener("pagehide", persistMusicPlayerState);
      window.clearInterval(timePersistInterval);
    };
  });

  $effect(() => {
    if (!musicPlayerPersistenceReady) return;
    const snapshot = musicPlayerSnapshot();
    if (snapshot === lastMusicPlayerSnapshot) return;

    const timeout = window.setTimeout(() => {
      persistMusicPlayerState();
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

{#if bareShell}
  {@render pageContent()}
{:else}
  <div
    class="flex min-h-dvh"
    style:--prismedia-canvas-header-height="3.5rem"
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
{/if}
