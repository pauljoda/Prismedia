<script lang="ts">
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { cn, prefersReducedMotion } from "@prismedia/ui-svelte";
  import { useNavCustomization } from "$lib/stores/nav-customization.svelte";
  import { appShellNavIconMap } from "./app-shell-nav-icon-map";
  import MobileMoreSheet from "./MobileMoreSheet.svelte";
  import MobileMoreNavButton from "./MobileMoreNavButton.svelte";

  const nav = useNavCustomization();
  const favorites = $derived(nav.resolvedFavorites);
  const favoriteHrefs = $derived(new Set(favorites.map((tab) => tab.href)));

  const pathname = $derived(page.url.pathname);

  function isActive(href: string): boolean {
    return pathname === href || (href !== "/" && pathname.startsWith(href + "/"));
  }

  // --- Bottom-sheet open state -------------------------------------------------
  // `progress` is 0 (closed) → 1 (fully open). While `dragging` the sheet follows
  // the finger with no transition; otherwise it animates between snap points.
  // `mounted` keeps the sheet in the DOM and is cleared deterministically by a
  // timer (never relying on a transitionend that might not fire).
  const CLOSE_MS = 320;
  let mounted = $state(false);
  let progress = $state(0);
  let dragging = $state(false);
  let closeTimer: ReturnType<typeof setTimeout> | null = null;
  const isOpen = $derived(mounted && progress > 0.5);

  const isMoreActive = $derived(
    isOpen ||
      nav.resolvedSections.some((section) =>
        section.items.some((item) => !favoriteHrefs.has(item.href) && isActive(item.href)),
      ),
  );

  function refHeight(): number {
    return Math.min(window.innerHeight * 0.55, 480);
  }

  function clamp01(v: number): number {
    return Math.max(0, Math.min(1, v));
  }

  function clearCloseTimer() {
    if (closeTimer) {
      clearTimeout(closeTimer);
      closeTimer = null;
    }
  }

  function settleOpen() {
    clearCloseTimer();
    dragging = false;
    // Enable the transition for a painted frame, then animate to fully open.
    requestAnimationFrame(() => requestAnimationFrame(() => (progress = 1)));
  }

  function settleClosed() {
    dragging = false;
    requestAnimationFrame(() => requestAnimationFrame(() => (progress = 0)));
    // Deterministic unmount — independent of any transition firing.
    clearCloseTimer();
    closeTimer = setTimeout(() => {
      mounted = false;
      progress = 0;
      closeTimer = null;
    }, CLOSE_MS);
  }

  function openSheet() {
    clearCloseTimer();
    mounted = true;
    settleOpen();
  }

  function closeSheet() {
    if (!mounted) return;
    settleClosed();
  }

  function toggleSheet() {
    if (isOpen) closeSheet();
    else openSheet();
  }

  // Shared window-listener helpers so a gesture can never leave a listener
  // attached (which previously caused the bar to lock up).
  function addWindow(move: (e: PointerEvent) => void, up: (e: PointerEvent) => void) {
    window.addEventListener("pointermove", move, { passive: false });
    window.addEventListener("pointerup", up);
    window.addEventListener("pointercancel", up);
  }
  function removeWindow(move: (e: PointerEvent) => void, up: (e: PointerEvent) => void) {
    window.removeEventListener("pointermove", move);
    window.removeEventListener("pointerup", up);
    window.removeEventListener("pointercancel", up);
  }

  // --- Swipe-up-to-open gesture (anywhere on the bar) --------------------------
  let barPointer: number | null = null;
  let barStartX = 0;
  let barStartY = 0;
  let swiping = false;
  let suppressClick = false;

  function barPointerDown(e: PointerEvent) {
    if (isOpen) return; // when open, taps behave normally
    barPointer = e.pointerId;
    barStartX = e.clientX;
    barStartY = e.clientY;
    swiping = false;
    addWindow(barPointerMove, barPointerUp);
  }

  function barPointerMove(e: PointerEvent) {
    if (e.pointerId !== barPointer) return;
    const up = barStartY - e.clientY;
    const dx = Math.abs(e.clientX - barStartX);
    if (!swiping) {
      if (up > 8 && up > dx) {
        swiping = true;
        suppressClick = true;
        clearCloseTimer();
        mounted = true;
        dragging = true;
      } else if (up < -4 || dx > 12) {
        cleanupBar();
        return;
      } else {
        return;
      }
    }
    e.preventDefault();
    progress = clamp01(up / refHeight());
  }

  function barPointerUp(e: PointerEvent) {
    if (barPointer !== null && e.pointerId !== barPointer) return;
    const wasSwiping = swiping;
    cleanupBar();
    if (wasSwiping) {
      if (progress > 0.3) settleOpen();
      else settleClosed();
    }
  }

  function cleanupBar() {
    barPointer = null;
    swiping = false;
    removeWindow(barPointerMove, barPointerUp);
    // Never let the click-suppression linger and eat a later tap.
    setTimeout(() => (suppressClick = false), 400);
  }

  // Swallow the click that follows a swipe so the underlying tab/button is not
  // also triggered.
  function barClickCapture(e: MouseEvent) {
    if (suppressClick) {
      e.preventDefault();
      e.stopPropagation();
      suppressClick = false;
    }
  }

  // --- Drag-down-to-dismiss gesture (from the sheet handle) --------------------
  let closePointer: number | null = null;
  let closeStartY = 0;

  function startCloseDrag(e: PointerEvent) {
    closePointer = e.pointerId;
    closeStartY = e.clientY;
    clearCloseTimer();
    dragging = true;
    addWindow(closeDragMove, closeDragUp);
  }

  function closeDragMove(e: PointerEvent) {
    if (e.pointerId !== closePointer) return;
    const down = e.clientY - closeStartY;
    e.preventDefault();
    progress = clamp01(1 - down / refHeight());
  }

  function closeDragUp(e: PointerEvent) {
    if (closePointer !== null && e.pointerId !== closePointer) return;
    closePointer = null;
    removeWindow(closeDragMove, closeDragUp);
    if (progress > 0.6) settleOpen();
    else settleClosed();
  }
</script>

<nav
  class="mobile-nav app-glass fixed inset-x-0 bottom-0 z-50 flex items-stretch justify-around border-t md:hidden"
  onpointerdown={barPointerDown}
  onclickcapture={barClickCapture}
>
  {#each favorites as tab (tab.href)}
    {@const active = isActive(tab.href)}
    {@const Icon = appShellNavIconMap[tab.icon]}
    <a
      href={resolve(tab.href as "/")}
      aria-current={active ? "page" : undefined}
      class={cn(
        "flex flex-1 flex-col items-center justify-center gap-1 px-2 py-2 text-[0.65rem] transition-colors duration-fast",
        active ? "mobile-item-active" : "text-text-disabled hover:text-text-muted",
      )}
      style:--nav-accent={tab.accent}
    >
      {#if Icon}
        <Icon class="h-5 w-5" />
      {/if}
      <span class="max-w-full truncate">{tab.label}</span>
    </a>
  {/each}

  <MobileMoreNavButton {isMoreActive} sheetOpen={isOpen} onToggleSheet={toggleSheet} />
</nav>

<MobileMoreSheet
  {mounted}
  {progress}
  {dragging}
  reduceMotion={prefersReducedMotion()}
  onClose={closeSheet}
  onHandlePointerDown={startCloseDrag}
/>

<style>
  /* Roomier bar that clears the iPhone home indicator / screen curve.
     content-box keeps min-height as the icon area only, so the safe-area
     padding-bottom is added on top and the bar height stays deterministic
     (mirrored by --prismedia-mobile-bottom-clearance in the layout).
     touch-action:none lets the swipe-up gesture own vertical drags. */
  .mobile-nav {
    box-sizing: content-box;
    min-height: 3.25rem;
    padding-top: 0.4rem;
    padding-bottom: max(1.25rem, env(safe-area-inset-bottom, 0px));
    touch-action: none;
  }

  .mobile-item-active {
    color: var(--nav-accent);
  }
</style>
