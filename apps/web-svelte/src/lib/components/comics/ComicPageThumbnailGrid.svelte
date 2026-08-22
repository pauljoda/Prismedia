<script lang="ts">
  import type { EntityReaderManifestPage } from "$lib/api/generated/model";
  import { entityReaderPageUrl } from "$lib/api/entity-reader";

  interface Props {
    entityId: string;
    entityTitle: string;
    pages: EntityReaderManifestPage[];
    returnHref: string;
  }

  let { entityId, entityTitle, pages, returnHref }: Props = $props();

  function ordinalOf(page: EntityReaderManifestPage): number {
    return Number(page.ordinal);
  }

  function readerHref(page: EntityReaderManifestPage): string {
    const ordinal = ordinalOf(page);
    const params = new URLSearchParams({ returnTo: returnHref, index: String(ordinal) });
    return `/entities/${entityId}/reader?${params}`;
  }

  function aspectRatio(page: EntityReaderManifestPage): string | undefined {
    const width = Number(page.width);
    const height = Number(page.height);
    return width > 0 && height > 0 ? `${width} / ${height}` : undefined;
  }

  function pageTypeLabel(value: string): string | null {
    return value === "story"
      ? null
      : value.replaceAll("-", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
  }
</script>

<div class="comic-page-grid" aria-label={`Pages in ${entityTitle}`}>
  {#each pages as manifestPage (manifestPage.ordinal)}
    {@const ordinal = ordinalOf(manifestPage)}
    {@const typeLabel = pageTypeLabel(manifestPage.pageType)}
    <a
      class="comic-page-card"
      href={readerHref(manifestPage)}
      aria-label={`Open page ${ordinal + 1} of ${entityTitle}`}
    >
      <span class="page-artwork" style:aspect-ratio={aspectRatio(manifestPage)}>
        <img
          src={entityReaderPageUrl(entityId, ordinal)}
          alt={`Page ${ordinal + 1}`}
          loading="lazy"
          decoding="async"
        />
        <span class="page-number">{ordinal + 1}</span>
        {#if typeLabel}
          <span class="page-type">{typeLabel}</span>
        {/if}
      </span>
    </a>
  {/each}
</div>

<style>
  .comic-page-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(8rem, 1fr));
    gap: 0.75rem;
  }

  .comic-page-card {
    min-width: 0;
    color: inherit;
    text-decoration: none;
  }

  .page-artwork {
    position: relative;
    display: block;
    overflow: hidden;
    aspect-ratio: 2 / 3;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-sm);
    background: var(--color-surface-1);
    box-shadow: var(--shadow-sm);
    transition: border-color 150ms ease, transform 150ms ease;
  }

  .comic-page-card:hover .page-artwork,
  .comic-page-card:focus-visible .page-artwork {
    border-color: var(--color-border-accent-strong);
    transform: translateY(-2px);
  }

  .comic-page-card:focus-visible {
    outline: none;
  }

  .page-artwork img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  .page-number,
  .page-type {
    position: absolute;
    bottom: 0.4rem;
    border: 1px solid rgb(255 255 255 / 0.14);
    border-radius: var(--radius-xs);
    background: rgb(0 0 0 / 0.76);
    padding: 0.15rem 0.35rem;
    color: #fff;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    line-height: 1;
    backdrop-filter: blur(var(--glass-blur-sm));
  }

  .page-number {
    left: 0.4rem;
  }

  .page-type {
    right: 0.4rem;
    text-transform: uppercase;
  }

  @media (max-width: 640px) {
    .comic-page-grid {
      grid-template-columns: repeat(auto-fill, minmax(6.75rem, 1fr));
      gap: 0.55rem;
    }
  }
</style>
