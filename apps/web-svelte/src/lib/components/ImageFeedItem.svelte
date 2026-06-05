<script lang="ts">
  import { Image as ImageIcon, Video as VideoIcon } from "@lucide/svelte";
  import type { ImageListItemDto } from "@prismedia/contracts";
  import { apiAssetUrl as toApiUrl } from "$lib/api/orval-fetch";
  import { elementInView } from "$lib/hooks/element-in-view.svelte";
  import NsfwBlur from "$lib/components/nsfw/NsfwBlur.svelte";

  interface Props {
    image: ImageListItemDto;
    index: number;
    isActive: boolean;
    shouldLoad: boolean;
    onOpen: (id: string) => void;
    onActive: (index: number) => void;
  }

  let { image, index, isActive, shouldLoad, onOpen, onActive }: Props = $props();

  const src = $derived(toApiUrl(image.thumbnailPath));
  const videoSrc = $derived(toApiUrl(image.previewPath ?? image.fullPath));
  const canPreview = $derived(image.isVideo && Boolean(videoSrc));
  const shouldRenderPreview = $derived(canPreview && shouldLoad);
  const activeDetector = elementInView({ rootMargin: "-18% 0px -18% 0px", threshold: 0.55 });
  let videoEl = $state<HTMLVideoElement | undefined>();

  $effect(() => {
    if (activeDetector.inView) onActive(index);
  });

  $effect(() => {
    if (!videoEl) return;
    videoEl.muted = true;
    videoEl.volume = 0;
    if (isActive && shouldRenderPreview) {
      void videoEl.play().catch(() => {});
    } else {
      videoEl.pause();
    }
  });
</script>

<article use:activeDetector.attach class="image-feed-item">
  <button
    type="button"
    onclick={() => onOpen(image.id)}
    class="image-feed-media"
    title={image.title}
  >
    {#if src}
      <NsfwBlur isNsfw={image.isNsfw} class="flex h-full w-full items-center justify-center">
        <img
          src={src}
          alt={image.title}
          loading={index < 2 ? "eager" : "lazy"}
          decoding="async"
          class="max-h-full max-w-full object-contain"
        />

        {#if shouldRenderPreview}
          <video
            bind:this={videoEl}
            src={videoSrc ?? undefined}
            muted
            loop
            playsinline
            preload="auto"
            aria-hidden="true"
            class={isActive
              ? "absolute inset-0 h-full w-full object-contain opacity-100 transition-opacity duration-fast"
              : "absolute inset-0 h-full w-full object-contain opacity-0 transition-opacity duration-fast"}
          ></video>
        {/if}
      </NsfwBlur>
    {:else}
      <div class="flex h-full w-full items-center justify-center text-text-disabled">
        <ImageIcon class="h-10 w-10" />
      </div>
    {/if}
    {#if image.isVideo}
      <span
        class="pointer-events-none absolute left-2 top-2 z-10 inline-flex items-center gap-1 border border-accent-500/30 bg-black/70 px-1.5 py-1 text-[0.58rem] font-mono uppercase tracking-[0.12em] text-accent-100"
        title="Animated image"
      >
        <VideoIcon class="h-3 w-3" />
        Animated
      </span>
    {/if}
  </button>
  <div class="image-feed-caption">
    <h3 class="truncate text-[0.78rem] font-medium text-text-primary">{image.title}</h3>
    {#if image.width && image.height}
      <span class="font-mono text-[0.62rem] text-text-disabled">
        {image.width}x{image.height}
      </span>
    {/if}
  </div>
</article>

<style>
  .image-feed-item {
    display: grid;
    gap: 0.35rem;
    margin-inline: auto;
    width: min(100%, 54rem);
    scroll-margin-top: 4.5rem;
  }
  .image-feed-media {
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    width: 100%;
    height: min(
      48rem,
      max(12rem, calc(100dvh - var(--prismedia-mobile-bottom-clearance) - 4.5rem))
    );
    overflow: hidden;
    border: 1px solid var(--color-border-subtle);
    background: #040507;
    box-shadow: var(--shadow-media-well);
  }
  .image-feed-caption {
    display: flex;
    min-width: 0;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    padding-inline: 0.25rem;
  }

  @media (min-width: 768px) {
    .image-feed-media {
      height: min(
        52rem,
        max(18rem, calc(100dvh - var(--prismedia-desktop-bottom-clearance) - 5rem))
      );
    }
  }
</style>
