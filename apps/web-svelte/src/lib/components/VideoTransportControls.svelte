<script lang="ts">
  import { Button } from "@prismedia/ui-svelte";
  import { Loader, Pause, Play, RotateCcw, RotateCw } from "@lucide/svelte";

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
  const skipClass = "relative rounded-full";
  const playClass = $derived(`rounded-full ${isMobile ? "size-11" : "size-10"}`);
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
  <Button variant="outline" size="icon"
    type="button"
    onclick={(event) => handleSeek(event, -10)}
    class={skipClass}
    title="Skip back 10s"
    aria-label="Skip back 10s"
  >
    <RotateCcw class="h-4 w-4" />
    <span class={skipLabelClass}>10</span>
  </Button>
  <Button variant="default" size="icon"
    type="button"
    onclick={handleTogglePlay}
    class={playClass}
    aria-label={playing ? "Pause" : "Play"}
  >
    {#if buffering}
      <Loader
        class="h-4 w-4 animate-spin"
        role="status"
        aria-label="Loading video"
      />
    {:else if playing}
      <Pause class="h-4 w-4" fill="currentColor" />
    {:else}
      <Play class="ml-0.5 size-4" fill="currentColor" />
    {/if}
  </Button>
  <Button variant="outline" size="icon"
    type="button"
    onclick={(event) => handleSeek(event, 10)}
    class={skipClass}
    title="Skip forward 10s"
    aria-label="Skip forward 10s"
  >
    <RotateCw class="h-4 w-4" />
    <span class={skipLabelClass}>10</span>
  </Button>
</div>
