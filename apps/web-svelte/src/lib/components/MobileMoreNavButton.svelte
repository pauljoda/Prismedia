<script lang="ts">
  import { ChevronUp } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  interface Props {
    isMoreActive: boolean;
    sheetOpen: boolean;
    onToggleSheet: () => void;
  }

  let { isMoreActive, sheetOpen, onToggleSheet }: Props = $props();

  const nsfw = useNsfw();

  const LONG_PRESS_MS = 2000;
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

<Button variant="ghost" size="sm"
  type="button"
  class={cn(
    "h-auto min-h-control-lg flex flex-1 cursor-pointer select-none touch-manipulation flex-col items-center justify-center gap-1 rounded-none px-2 py-2 text-caption [&_svg]:size-5 transition-colors duration-fast",
    isMoreActive ? "text-text-accent" : "text-text-disabled hover:text-text-muted",
  )}
  style="-webkit-touch-callout:none"
  aria-label="More navigation. Press and hold two seconds to toggle SFW and full NSFW."
  aria-expanded={sheetOpen}
  onclick={handleClick}
  onpointerdown={handlePointerDown}
  onpointermove={handlePointerMove}
  onpointerup={endPointer}
  onpointercancel={endPointer}
  onlostpointercapture={clearLongPress}
>
  <ChevronUp class={cn("size-5 transition-transform motion-reduce:transition-none", sheetOpen && "rotate-180")} />
  <span>{sheetOpen ? "Close" : "More"}</span>
</Button>
