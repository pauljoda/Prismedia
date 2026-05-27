<script lang="ts">
  import { Loader, Pause, RotateCcw, RotateCw } from "@lucide/svelte";

  interface Props {
    buffering?: boolean;
    playing?: boolean;
    variant?: "desktop" | "mobile";
    onSeek: (delta: number) => void;
    onTogglePlay: () => void;
  }

  let {
    buffering = false,
    playing = false,
    variant = "desktop",
    onSeek,
    onTogglePlay,
  }: Props = $props();

  const isMobile = $derived(variant === "mobile");
  const wrapperClass = $derived(isMobile
    ? "pointer-events-auto flex items-center gap-4"
    : "hidden items-center gap-2.5 sm:flex");
  const skipClass = $derived(
    `player-skip-button relative flex ${isMobile ? "h-8 w-8 text-white/72" : "h-8 w-8 text-white/70"} items-center justify-center rounded-full transition-all hover:text-white`,
  );
  const playClass = $derived(
    `player-play-button flex ${isMobile ? "h-11 w-11" : "h-10 w-10"} items-center justify-center rounded-full text-accent-950 transition-all`,
  );
  const skipLabelClass = $derived(`absolute mt-[1px] ${isMobile ? "text-[0.42rem]" : "text-[0.45rem]"} font-bold`);

  function handleSeek(event: MouseEvent, delta: number) {
    if (isMobile) event.stopPropagation();
    onSeek(delta);
  }

  function handleTogglePlay(event: MouseEvent) {
    if (isMobile) event.stopPropagation();
    onTogglePlay();
  }
</script>

<div class={wrapperClass}>
  <button
    type="button"
    onclick={(event) => handleSeek(event, -10)}
    class={skipClass}
    title="Skip back 10s"
    aria-label="Skip back 10s"
  >
    <RotateCcw class="h-4 w-4" />
    <span class={skipLabelClass}>10</span>
  </button>
  <button
    type="button"
    onclick={handleTogglePlay}
    class={playClass}
    aria-label={playing ? "Pause" : "Play"}
  >
    {#if buffering}
      <Loader class="h-4 w-4 animate-spin" />
    {:else if playing}
      <Pause class="h-4 w-4" fill="currentColor" />
    {:else}
      <span class="play-glyph" aria-hidden="true"></span>
    {/if}
  </button>
  <button
    type="button"
    onclick={(event) => handleSeek(event, 10)}
    class={skipClass}
    title="Skip forward 10s"
    aria-label="Skip forward 10s"
  >
    <RotateCw class="h-4 w-4" />
    <span class={skipLabelClass}>10</span>
  </button>
</div>

<style>
  .player-play-button {
    background: linear-gradient(135deg, var(--color-accent-300) 0%, var(--color-accent-500) 100%);
    border: 1px solid rgba(255, 255, 255, 0.20);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.25),
      0 0 0 2px rgba(196, 154, 90, 0.25),
      0 0 24px rgba(196, 154, 90, 0.40),
      0 0 48px rgba(196, 154, 90, 0.15);
  }

  .player-play-button:hover {
    background: linear-gradient(135deg, var(--color-accent-200) 0%, var(--color-accent-400) 100%);
    border-color: rgba(255, 255, 255, 0.30);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.30),
      0 0 0 2px rgba(196, 154, 90, 0.40),
      0 0 32px rgba(196, 154, 90, 0.55),
      0 0 64px rgba(196, 154, 90, 0.20);
  }

  .player-skip-button {
    backdrop-filter: blur(var(--glass-blur-sm));
    -webkit-backdrop-filter: blur(var(--glass-blur-sm));
    background: var(--color-white-overlay);
    border: 1px solid rgba(255, 255, 255, 0.12);
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
  }

  .player-skip-button:hover {
    background: rgba(255, 255, 255, 0.14);
    border-color: rgba(255, 255, 255, 0.22);
    box-shadow:
      0 0 12px rgba(255, 255, 255, 0.08),
      0 2px 8px rgba(0, 0, 0, 0.30);
  }

  .play-glyph {
    display: block;
    height: 0.875rem;
    position: relative;
    width: 0.875rem;
  }

  .play-glyph::before {
    border-bottom: 0.36rem solid transparent;
    border-left: 0.56rem solid currentColor;
    border-top: 0.36rem solid transparent;
    content: "";
    left: 50%;
    position: absolute;
    top: 50%;
    transform: translate(-42%, -50%);
  }
</style>
