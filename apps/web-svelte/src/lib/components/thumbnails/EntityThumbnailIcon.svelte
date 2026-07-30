<script lang="ts">
  import type { EntityThumbnailMetaIcon } from "$lib/entities/entity-thumbnail";
  import { THUMBNAIL_META_ICON } from "$lib/api/generated/codes";
  import {
    thumbnailMetaIcon,
    thumbnailPlaceholderIcon,
  } from "$lib/entities/entity-kind-icons";
  import { Disc3, Film, Music } from "@lucide/svelte";

  interface Props {
    icon: EntityThumbnailMetaIcon;
    size?: number;
    variant?: "meta" | "placeholder";
  }

  let { icon, size = 12, variant = "meta" }: Props = $props();

  const MetaIcon = $derived(thumbnailMetaIcon(icon));
  const PlaceholderIcon = $derived(thumbnailPlaceholderIcon(icon));
</script>

{#if variant === "meta"}
  <MetaIcon {size} />
{:else if icon === THUMBNAIL_META_ICON.video}
  <div class="placeholder-frame"><Film class="placeholder-icon-framed" /></div>
{:else if icon === THUMBNAIL_META_ICON.audio}
  <div class="placeholder-audio">
    <Disc3 class="placeholder-disc" />
    <Music class="placeholder-note" />
  </div>
{:else}
  <PlaceholderIcon class="placeholder-icon" />
{/if}

<style>
  .placeholder-frame {
    display: flex;
    width: 3.5rem;
    height: 3.5rem;
    align-items: center;
    justify-content: center;
    border: 1px solid var(--color-border-default);
    background: var(--color-surface-2);
    box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.08), 0 0 24px rgb(0 0 0 / 0.35);
  }
  :global(.placeholder-icon-framed) {
    width: 1.75rem;
    height: 1.75rem;
    color: var(--color-text-muted);
  }
  :global(.placeholder-icon) {
    width: 2rem;
    height: 2rem;
    color: rgb(255 255 255 / 0.25);
  }
  .placeholder-audio {
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  :global(.placeholder-disc) {
    width: 3.5rem;
    height: 3.5rem;
    color: rgb(255 255 255 / 0.15);
  }
  :global(.placeholder-note) {
    position: absolute;
    width: 1.5rem;
    height: 1.5rem;
    color: rgb(255 255 255 / 0.4);
  }

  @container (max-width: 112px) {
    :global(.placeholder-icon) { width: 1.35rem; height: 1.35rem; }
    .placeholder-frame { width: 2rem; height: 2rem; }
    :global(.placeholder-icon-framed) { width: 1.05rem; height: 1.05rem; }
    :global(.placeholder-disc) { width: 2.1rem; height: 2.1rem; }
    :global(.placeholder-note) { width: 0.95rem; height: 0.95rem; }
  }
</style>
