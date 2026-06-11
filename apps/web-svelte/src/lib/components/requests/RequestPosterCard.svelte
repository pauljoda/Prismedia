<script lang="ts">
  import { Check, Disc3, Film, Music, Star, Users } from "@lucide/svelte";

  /**
   * EntityThumbnail-style poster card for external request items (provider search
   * results and discography entries). External items have no library entity or
   * capabilities, so this mirrors the look — media box, glass info strip, mono
   * chips, brass tracked badge — without the library coupling.
   */
  interface Props {
    href: string;
    title: string;
    subtitle?: string | null;
    imageUrl?: string | null;
    /** CSS aspect-ratio of the artwork box, e.g. "2 / 3" for posters, "1 / 1" for music. */
    aspect?: string;
    /** Short mono chips under the title: year, type, runtime, certification… */
    chips?: (string | null | undefined)[];
    /** "In Radarr"-style label when the item is already tracked upstream. */
    trackedLabel?: string | null;
    rating?: number | null;
    /** Placeholder glyph family when there is no artwork. */
    placeholder?: "video" | "music" | "person";
  }

  let {
    href,
    title,
    subtitle = null,
    imageUrl = null,
    aspect = "2 / 3",
    chips = [],
    trackedLabel = null,
    rating = null,
    placeholder = "video",
  }: Props = $props();

  let imageFailed = $state(false);
  const visibleChips = $derived(chips.filter((chip): chip is string => Boolean(chip)).slice(0, 3));
  const showImage = $derived(Boolean(imageUrl) && !imageFailed);
  const ratingLabel = $derived(rating !== null && rating > 0 ? rating.toFixed(1) : null);

  // New artwork URL → give the image another chance (cards are reused across searches).
  $effect(() => {
    void imageUrl;
    imageFailed = false;
  });
</script>

<a {href} class="request-card" aria-label={title}>
  <div class="media" style:aspect-ratio={aspect}>
    {#if showImage}
      <img
        src={imageUrl}
        alt=""
        loading="lazy"
        onerror={() => (imageFailed = true)}
      />
    {:else if placeholder === "music"}
      <div class="placeholder placeholder-audio" aria-hidden="true">
        <Disc3 class="placeholder-disc" />
        <Music class="placeholder-note" />
      </div>
    {:else if placeholder === "person"}
      <div class="placeholder" aria-hidden="true"><Users class="placeholder-icon" /></div>
    {:else}
      <div class="placeholder" aria-hidden="true"><Film class="placeholder-icon" /></div>
    {/if}

    {#if trackedLabel}
      <span class="tracked-badge" title={trackedLabel}>
        <Check class="tracked-check" aria-hidden="true" />
        {trackedLabel}
      </span>
    {/if}
    {#if ratingLabel}
      <span class="rating-badge" title={`Rating ${ratingLabel}`}>
        <Star class="rating-star" aria-hidden="true" />
        {ratingLabel}
      </span>
    {/if}
  </div>

  <div class="glass-info">
    <h3 {title}>{title}</h3>
    {#if subtitle}
      <p class="subtitle" title={subtitle}>{subtitle}</p>
    {/if}
    {#if visibleChips.length > 0}
      <div class="chips">
        {#each visibleChips as chip (chip)}
          <span class="chip">{chip}</span>
        {/each}
      </div>
    {/if}
  </div>
</a>

<style>
  .request-card {
    position: relative;
    display: flex;
    flex-direction: column;
    min-width: 0;
    overflow: hidden;
    color: var(--color-text, #f4efe6);
    text-decoration: none;
    border: 1px solid var(--color-border-subtle, rgb(255 255 255 / 0.08));
    border-radius: 6px;
    box-shadow: var(--shadow-card);
    transition:
      transform 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      border-color 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .request-card:is(:hover, :focus-visible) {
    transform: translateY(-1px);
    border-color: var(--color-border-accent, rgb(242 194 106 / 0.32));
    box-shadow:
      var(--shadow-card-hover),
      0 0 12px rgb(242 194 106 / 0.07),
      0 0 4px rgb(242 194 106 / 0.1);
  }

  @media (prefers-reduced-motion: reduce) {
    .request-card {
      transition: none;
    }

    .request-card:is(:hover, :focus-visible) {
      transform: none;
    }
  }

  .media {
    position: relative;
    width: 100%;
    overflow: hidden;
    border-radius: 5px 5px 0 0;
    background:
      radial-gradient(circle at 50% 45%, rgb(255 255 255 / 0.08), transparent 34%),
      linear-gradient(135deg, rgb(15 16 18 / 0.96), rgb(28 25 20 / 0.92)),
      #111;
  }

  .media img {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: cover;
    object-position: center;
  }

  .placeholder {
    position: absolute;
    inset: 0;
    display: grid;
    place-items: center;
    color: rgb(244 239 230 / 0.18);
  }

  .placeholder :global(.placeholder-icon) {
    width: 28%;
    height: 28%;
  }

  .placeholder-audio :global(.placeholder-disc) {
    width: 42%;
    height: 42%;
    color: rgb(244 239 230 / 0.14);
  }

  .placeholder-audio :global(.placeholder-note) {
    position: absolute;
    width: 16%;
    height: 16%;
    transform: translate(85%, -85%);
    color: rgb(242 194 106 / 0.28);
  }

  .tracked-badge {
    position: absolute;
    top: 0.45rem;
    left: 0.45rem;
    z-index: 2;
    display: inline-flex;
    align-items: center;
    gap: 0.22rem;
    padding: 0.16rem 0.4rem 0.16rem 0.3rem;
    border: 1px solid rgb(242 194 106 / 0.45);
    border-radius: var(--radius-xs, 4px);
    background: linear-gradient(135deg, rgb(38 31 18 / 0.92), rgb(24 20 13 / 0.94));
    color: var(--color-accent-300, #f2c26a);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
    line-height: 1;
    white-space: nowrap;
    box-shadow: 0 0 8px rgb(242 194 106 / 0.22);
  }

  .tracked-badge :global(.tracked-check) {
    width: 0.7rem;
    height: 0.7rem;
  }

  .rating-badge {
    position: absolute;
    right: 0.45rem;
    bottom: 0.45rem;
    z-index: 2;
    display: inline-flex;
    align-items: center;
    gap: 0.2rem;
    padding: 0.14rem 0.32rem;
    border: 1px solid rgb(255 255 255 / 0.12);
    border-radius: var(--radius-xs, 4px);
    background: rgb(10 10 12 / 0.78);
    color: rgb(244 239 230 / 0.85);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
    line-height: 1;
  }

  .rating-badge :global(.rating-star) {
    width: 0.62rem;
    height: 0.62rem;
    color: var(--color-accent-300, #f2c26a);
  }

  .glass-info {
    display: flex;
    flex: 1;
    flex-direction: column;
    gap: 0.2rem;
    min-width: 0;
    padding: 0.5rem 0.6rem;
    border-top: 1px solid rgb(255 255 255 / 0.05);
    background: linear-gradient(180deg, rgb(20 22 26 / 0.95) 0%, rgb(13 14 17) 100%);
  }

  /* Titles get up to two lines before truncating — single-line cut too much off. */
  .glass-info h3 {
    display: -webkit-box;
    margin: 0;
    overflow: hidden;
    font-size: 0.78rem;
    font-weight: 500;
    line-height: 1.25;
    -webkit-box-orient: vertical;
    -webkit-line-clamp: 2;
    line-clamp: 2;
  }

  .subtitle {
    margin: 0;
    overflow: hidden;
    color: rgb(244 239 230 / 0.55);
    font-size: 0.66rem;
    line-height: 1.2;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 0.25rem;
    margin-top: 0.1rem;
    max-block-size: 1.4rem;
    overflow: hidden;
  }

  .chip {
    display: inline-flex;
    align-items: center;
    min-width: 0;
    max-width: 100%;
    padding: 0.12rem 0.28rem;
    overflow: hidden;
    border: 1px solid rgb(255 255 255 / 0.1);
    border-radius: var(--radius-xs, 4px);
    background: rgb(255 255 255 / 0.06);
    color: rgb(244 239 230 / 0.72);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
    line-height: 1;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
</style>
