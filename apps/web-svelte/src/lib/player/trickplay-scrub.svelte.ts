import { loadTrickplayFrames, type TrickplayFrame } from "@prismedia/ui-svelte";

export interface ScrubSource {
  trickplaySprite?: string;
  trickplayVtt?: string;
  scrubDurationSeconds?: number;
}

/**
 * Reactive trickplay-scrub state for a single thumbnail surface. Creates one
 * instance per thumbnail in the component's `<script lang="ts">` block.
 *
 * Consumers bind pointer/touch handlers to the thumbnail element and read
 * `activeFrame` + `spriteDims` to render the scrub overlay.
 */
export function createTrickplayScrub(getSource: () => ScrubSource) {
  let frames = $state<TrickplayFrame[] | null>(null);
  let trickplayError = $state(false);
  let activeFrameIndex = $state<number | null>(null);
  let lastFrameIndex: number | null = null;
  let touchScrubbing = false;
  let touchStartPos: { x: number; y: number } | null = null;
  let touchLocked: "scrub" | "scroll" | null = null;
  const LOCK_THRESHOLD = 8;

  const source = $derived(getSource());

  const enabled = $derived(
    Boolean(
      source.trickplaySprite &&
        source.trickplayVtt &&
        source.scrubDurationSeconds &&
        source.scrubDurationSeconds > 0,
    ) && !trickplayError,
  );

  const activeFrame = $derived(
    activeFrameIndex != null && frames && activeFrameIndex < frames.length
      ? frames[activeFrameIndex]
      : null,
  );

  const spriteDims = $derived.by(() => {
    if (!frames) return { spriteWidth: 0, spriteHeight: 0 };
    return {
      spriteWidth: frames.reduce((max, f) => Math.max(max, f.x + f.width), 0),
      spriteHeight: frames.reduce((max, f) => Math.max(max, f.y + f.height), 0),
    };
  });

  async function ensureLoaded() {
    if (!enabled || frames || trickplayError) return;
    const vtt = source.trickplayVtt;
    const sprite = source.trickplaySprite;
    if (!vtt) return;
    try {
      if (sprite) {
        const img = new Image();
        img.src = sprite;
      }
      const next = await loadTrickplayFrames(vtt);
      frames = next;
    } catch {
      trickplayError = true;
    }
  }

  function updateActiveFrame(normalizedPosition: number) {
    if (!frames || !source.scrubDurationSeconds) return;
    const clamped = Math.max(0, Math.min(1, normalizedPosition));
    const targetTime = clamped * source.scrubDurationSeconds;
    let next = frames.findIndex(
      (f) => targetTime >= f.start && targetTime < f.end,
    );
    if (next === -1) {
      next = Math.min(frames.length - 1, Math.floor(clamped * frames.length));
    }
    if (lastFrameIndex === next) return;
    lastFrameIndex = next;
    activeFrameIndex = next;
  }

  function clear() {
    lastFrameIndex = null;
    activeFrameIndex = null;
  }

  function handlePointerEnter() {
    void ensureLoaded();
  }

  function handlePointerMove(event: PointerEvent, el: HTMLElement | undefined) {
    if (!enabled || !el) return;
    const bounds = el.getBoundingClientRect();
    if (bounds.width === 0) return;
    updateActiveFrame((event.clientX - bounds.left) / bounds.width);
  }

  function handlePointerLeave() {
    clear();
  }

  function handleTouchStart(event: TouchEvent) {
    if (!enabled) return;
    const t = event.touches[0];
    touchStartPos = { x: t.clientX, y: t.clientY };
    touchLocked = null;
    touchScrubbing = false;
    void ensureLoaded();
  }

  function handleTouchMove(event: TouchEvent, el: HTMLElement | undefined) {
    if (!enabled || !touchStartPos || !el) return;
    const t = event.touches[0];
    const dx = Math.abs(t.clientX - touchStartPos.x);
    const dy = Math.abs(t.clientY - touchStartPos.y);
    if (!touchLocked && (dx > LOCK_THRESHOLD || dy > LOCK_THRESHOLD)) {
      touchLocked = dx >= dy ? "scrub" : "scroll";
    }
    if (touchLocked === "scroll") return;
    event.preventDefault();
    touchScrubbing = true;
    const bounds = el.getBoundingClientRect();
    if (bounds.width === 0) return;
    updateActiveFrame((t.clientX - bounds.left) / bounds.width);
  }

  function handleTouchEnd() {
    touchStartPos = null;
    touchLocked = null;
    if (touchScrubbing) {
      touchScrubbing = false;
      clear();
    }
  }

  return {
    get enabled() {
      return enabled;
    },
    get activeFrame() {
      return activeFrame;
    },
    get spriteDims() {
      return spriteDims;
    },
    handlePointerEnter,
    handlePointerMove,
    handlePointerLeave,
    handleTouchStart,
    handleTouchMove,
    handleTouchEnd,
  };
}
