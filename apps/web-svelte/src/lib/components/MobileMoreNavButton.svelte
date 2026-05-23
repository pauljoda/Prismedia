<script lang="ts">
  import { MoreHorizontal } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  interface Props {
    isMoreActive: boolean;
    sheetOpen: boolean;
    onToggleSheet: () => void;
  }

  let { isMoreActive, sheetOpen, onToggleSheet }: Props = $props();

  const nsfw = useNsfw();

  const LONG_PRESS_MS = 5000;
  const MOVE_CANCEL_PX = 14;

  let timer: ReturnType<typeof setTimeout> | null = null;
  let start: { x: number; y: number } | null = null;
  let suppressClick = false;

  function clearLongPress() {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
    start = null;
  }

  function handlePointerDown(e: PointerEvent & { currentTarget: EventTarget & HTMLButtonElement }) {
    if (e.button !== 0) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    start = { x: e.clientX, y: e.clientY };
    timer = setTimeout(() => {
      timer = null;
      start = null;
      suppressClick = true;
      nsfw.toggleShowOff();
      try {
        navigator.vibrate?.(20);
      } catch {
        // Ignore unavailable vibration API.
      }
    }, LONG_PRESS_MS);
  }

  function handlePointerMove(e: PointerEvent) {
    if (!start || timer === null) return;
    const dx = e.clientX - start.x;
    const dy = e.clientY - start.y;
    if (dx * dx + dy * dy > MOVE_CANCEL_PX * MOVE_CANCEL_PX) {
      clearLongPress();
    }
  }

  function endPointer(e: PointerEvent & { currentTarget: EventTarget & HTMLButtonElement }) {
    if (e.currentTarget.hasPointerCapture(e.pointerId)) {
      e.currentTarget.releasePointerCapture(e.pointerId);
    }
    clearLongPress();
  }

  function handleClick(e: MouseEvent) {
    if (suppressClick) {
      e.preventDefault();
      e.stopPropagation();
      suppressClick = false;
      return;
    }
    onToggleSheet();
  }

  $effect(() => {
    return () => clearLongPress();
  });
</script>

<button
  type="button"
  class={cn(
    "flex cursor-pointer select-none touch-manipulation flex-col items-center gap-0.5 px-3 py-1.5 text-[0.65rem] transition-colors duration-fast",
    isMoreActive ? "text-text-accent" : "text-text-disabled hover:text-text-muted",
  )}
  style="-webkit-touch-callout:none"
  aria-label="More navigation. Press and hold five seconds to toggle SFW and full NSFW."
  aria-expanded={sheetOpen}
  onclick={handleClick}
  onpointerdown={handlePointerDown}
  onpointermove={handlePointerMove}
  onpointerup={endPointer}
  onpointercancel={endPointer}
  onlostpointercapture={clearLongPress}
>
  <MoreHorizontal class="h-5 w-5" />
  <span>More</span>
</button>
