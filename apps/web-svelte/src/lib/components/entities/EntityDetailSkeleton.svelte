<script lang="ts">
  import type { EntityDetailPosterSize } from "./EntityDetail.svelte";

  interface Props {
    posterSize?: EntityDetailPosterSize;
    showHero?: boolean;
    tabCount?: number;
  }

  let {
    posterSize = "large",
    showHero = true,
    tabCount = 3,
  }: Props = $props();
</script>

<article class="skeleton" data-poster-size={posterSize} aria-busy="true" aria-label="Loading">
  {#if showHero}
    <div class="skeleton-hero">
      <div class="skeleton-hero-gradient"></div>
      <div class="skeleton-hero-content">
        {#if posterSize !== "none"}
          <div class="skeleton-poster">
            <div class="shimmer"></div>
          </div>
        {/if}
        <div class="skeleton-text">
          <div class="skeleton-line title"></div>
          <div class="skeleton-line meta"></div>
          <div class="skeleton-line meta short"></div>
          <div class="skeleton-badges">
            <div class="skeleton-badge"></div>
            <div class="skeleton-badge"></div>
          </div>
        </div>
      </div>
    </div>
  {/if}

  <div class="skeleton-tabs">
    <div class="skeleton-tab-list">
      {#each { length: tabCount } as _, i (i)}
        <div class="skeleton-tab" class:active={i === 0} style:width={`${4 + (i % 2) * 1.5}rem`}></div>
      {/each}
    </div>
    <div class="skeleton-panel">
      <div class="skeleton-line field"></div>
      <div class="skeleton-line field wide"></div>
      <div class="skeleton-line field"></div>
      <div class="skeleton-line field narrow"></div>
    </div>
  </div>
</article>

<style>
  .skeleton {
    --sk-border: var(--color-border, #1c2235);
    --sk-surface: var(--color-surface-2, #101420);
    --sk-surface-raised: var(--color-surface-3, #151a28);
    --sk-shimmer: rgba(255, 255, 255, 0.04);
    --sk-shimmer-bright: rgba(255, 255, 255, 0.08);

    display: grid;
    gap: 0;
    min-width: 0;
    overflow: hidden;
  }

  /* ── Hero ───────────────────────────────────────────── */

  .skeleton-hero {
    position: relative;
    overflow: hidden;
    border-radius: var(--radius-md, 10px);
    min-height: 10rem;
  }

  .skeleton-hero-gradient {
    position: absolute;
    inset: 0;
    background: linear-gradient(
      135deg,
      rgba(20, 24, 35, 1) 0%,
      rgba(28, 34, 53, 0.8) 40%,
      rgba(20, 24, 35, 1) 100%
    );
  }

  .skeleton-hero-content {
    position: relative;
    display: flex;
    align-items: center;
    gap: 1.25rem;
    padding: 1.5rem;
    padding-top: 3rem;
    z-index: 1;
  }

  /* ── Poster ─────────────────────────────────────────── */

  .skeleton-poster {
    position: relative;
    flex-shrink: 0;
    width: var(--poster-width, 7rem);
    aspect-ratio: 2 / 3;
    border-radius: var(--radius-sm, 6px);
    background: #050505;
    box-shadow:
      0 8px 32px rgba(0, 0, 0, 0.6),
      0 0 0 1px rgba(196, 154, 90, 0.1);
    overflow: hidden;
  }

  [data-poster-size="small"] .skeleton-poster { --poster-width: 5rem; }
  [data-poster-size="medium"] .skeleton-poster { --poster-width: 7rem; }
  [data-poster-size="large"] .skeleton-poster { --poster-width: 10rem; }

  .shimmer {
    position: absolute;
    inset: 0;
    background: linear-gradient(
      110deg,
      transparent 25%,
      var(--sk-shimmer-bright) 37%,
      transparent 50%
    );
    background-size: 200% 100%;
    animation: shimmer 1.6s ease-in-out infinite;
  }

  /* ── Text lines ─────────────────────────────────────── */

  .skeleton-text {
    display: flex;
    flex-direction: column;
    gap: 0.6rem;
    flex: 1;
    min-width: 0;
  }

  .skeleton-line {
    height: 0.75rem;
    border-radius: var(--radius-xs, 4px);
    background: var(--sk-shimmer);
    animation: pulse 1.2s ease-in-out infinite;
  }

  .skeleton-line.title {
    height: 1.5rem;
    width: 55%;
    background: var(--sk-shimmer-bright);
  }

  .skeleton-line.meta {
    width: 38%;
  }

  .skeleton-line.meta.short {
    width: 22%;
  }

  .skeleton-badges {
    display: flex;
    gap: 0.5rem;
    margin-top: 0.25rem;
  }

  .skeleton-badge {
    width: 1.75rem;
    height: 1.75rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid var(--sk-border);
    background: var(--sk-shimmer);
    animation: pulse 1.2s ease-in-out infinite;
  }

  /* ── Tabs ───────────────────────────────────────────── */

  .skeleton-tabs {
    min-width: 0;
    margin-inline: 5px;
  }

  .skeleton-tab-list {
    display: flex;
    gap: 0.35rem;
    padding: 0.65rem 1.5rem;
    border: 1px solid var(--sk-border);
    border-top: 0;
    border-radius: 0 0 var(--radius-md, 10px) var(--radius-md, 10px);
    background: linear-gradient(180deg, rgba(19, 23, 31, 0.88), rgba(12, 15, 21, 0.96));
  }

  .skeleton-tab {
    height: 2rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid transparent;
    background: var(--sk-shimmer);
    animation: pulse 1.2s ease-in-out infinite;
  }

  .skeleton-tab.active {
    border-color: rgba(196, 154, 90, 0.15);
    background: rgba(196, 154, 90, 0.06);
  }

  /* ── Content panel ──────────────────────────────────── */

  .skeleton-panel {
    position: relative;
    z-index: 1;
    margin-top: -0.5rem;
    padding: 1.5rem;
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
    border: 1px solid var(--sk-border);
    border-top: 0;
    border-radius: 0 0 var(--radius-md, 10px) var(--radius-md, 10px);
    background:
      linear-gradient(180deg, rgba(19, 23, 31, 0.66), rgba(12, 15, 21, 0.86)),
      rgba(12, 15, 21, 0.72);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.035),
      0 10px 30px rgba(0, 0, 0, 0.24);
    min-height: 8rem;
  }

  .skeleton-line.field {
    width: 65%;
    animation-delay: 0.1s;
  }

  .skeleton-line.field.wide {
    width: 80%;
    animation-delay: 0.2s;
  }

  .skeleton-line.field.narrow {
    width: 40%;
    animation-delay: 0.3s;
  }

  /* ── Animations ─────────────────────────────────────── */

  @keyframes pulse {
    0%, 100% { opacity: 0.45; }
    50% { opacity: 0.85; }
  }

  @keyframes shimmer {
    0% { background-position: 200% 0; }
    100% { background-position: -200% 0; }
  }
</style>
