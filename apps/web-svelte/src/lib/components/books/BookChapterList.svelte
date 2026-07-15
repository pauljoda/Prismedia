<script lang="ts">
  import { BookOpen, Headphones, Layers2 } from "@lucide/svelte";
  import { Button, Panel } from "@prismedia/ui-svelte";
  import type { BookChapterRow } from "$lib/entities/book-chapter-list";

  interface Props {
    rows: readonly BookChapterRow[];
    primaryColor?: string;
    secondaryColor?: string;
    readingProgressLabel?: string | null;
    listeningProgressLabel?: string | null;
    onRead: (row: BookChapterRow) => void;
    onListen: (row: BookChapterRow) => void;
    onCombined: (row: BookChapterRow) => void;
  }

  let {
    rows,
    primaryColor = "var(--color-accent-400)",
    secondaryColor = "var(--color-accent-200)",
    readingProgressLabel = null,
    listeningProgressLabel = null,
    onRead,
    onListen,
    onCombined,
  }: Props = $props();
</script>

<section
  class="chapter-section"
  aria-labelledby="book-chapters-title"
  style:--reading-accent={primaryColor}
  style:--listening-accent={secondaryColor}
>
  <Panel variant="panel" class="chapter-panel">
    <div class="chapter-header">
      <div>
        <p class="chapter-kicker">Read &amp; listen</p>
        <h2 id="book-chapters-title">Chapters</h2>
      </div>
      <div class="chapter-legend" aria-label="Progress colors">
        <span><i class="legend-line reading"></i>Reading</span>
        <span><i class="legend-line listening"></i>Listening</span>
      </div>
    </div>

    <ol class="chapter-list">
      {#each rows as row, index (row.id)}
        <li
          class="chapter-row"
          class:is-current={row.isCurrentReading || row.isCurrentAudio}
          style:--chapter-depth={Math.min(row.depth, 3)}
        >
          <div class="progress-rails" aria-hidden="true">
            {#if row.isCurrentReading}
              <span class="progress-rail reading" data-testid={`reading-rail-${row.id}`}></span>
            {/if}
            {#if row.isCurrentAudio}
              <span class="progress-rail listening" data-testid={`listening-rail-${row.id}`}></span>
            {/if}
          </div>

          <span class="chapter-number">{String(index + 1).padStart(2, "0")}</span>
          <div class="chapter-copy">
            <h3>{row.title}</h3>
            {#if row.isCurrentReading || row.isCurrentAudio}
              <div class="current-labels" aria-live="polite">
                {#if row.isCurrentReading}
                  <span class="current-label reading">
                    Reading{readingProgressLabel ? ` · ${readingProgressLabel}` : " here"}
                  </span>
                {/if}
                {#if row.isCurrentAudio}
                  <span class="current-label listening">
                    Listening{listeningProgressLabel ? ` · ${listeningProgressLabel}` : " here"}
                  </span>
                {/if}
              </div>
            {/if}
          </div>

          <div class="chapter-actions">
            {#if row.readTarget}
              <Button
                variant="ghost"
                size="icon"
                class="chapter-action read-action"
                aria-label={`Read ${row.title}`}
                title={`Read ${row.title}`}
                onclick={() => onRead(row)}
              >
                <BookOpen class="h-4 w-4" />
              </Button>
            {/if}
            {#if row.audioTrack}
              <Button
                variant="ghost"
                size="icon"
                class="chapter-action listen-action"
                aria-label={`Listen to ${row.title}`}
                title={`Listen to ${row.title}`}
                onclick={() => onListen(row)}
              >
                <Headphones class="h-4 w-4" />
              </Button>
            {/if}
            {#if row.readTarget && row.audioTrack}
              <Button
                variant="ghost"
                size="icon"
                class="chapter-action combined-action"
                aria-label={`Read and listen to ${row.title}`}
                title={`Read & listen to ${row.title}`}
                onclick={() => onCombined(row)}
              >
                <Layers2 class="h-4 w-4" />
              </Button>
            {/if}
          </div>
        </li>
      {/each}
    </ol>
  </Panel>
</section>

<style>
  .chapter-section {
    min-width: 0;
  }

  :global(.chapter-panel) {
    overflow: hidden;
    border-color: color-mix(in srgb, var(--reading-accent) 18%, var(--color-border-default));
    background:
      linear-gradient(110deg, color-mix(in srgb, var(--reading-accent) 4%, transparent), transparent 38%),
      var(--color-surface-panel, #0c0f15);
  }

  .chapter-header {
    display: flex;
    align-items: end;
    justify-content: space-between;
    gap: 1rem;
    padding: 1rem 1rem 0.8rem;
    border-bottom: 1px solid var(--color-border-subtle);
  }

  .chapter-kicker {
    margin: 0 0 0.22rem;
    color: var(--color-text-disabled);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.58rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
  }

  h2 {
    margin: 0;
    color: var(--color-text-primary);
    font-family: var(--font-heading, "Geist", sans-serif);
    font-size: 1.05rem;
    font-weight: 600;
    letter-spacing: 0.01em;
  }

  .chapter-legend {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: 0.7rem;
    color: var(--color-text-muted);
    font-size: 0.64rem;
  }

  .chapter-legend span {
    display: inline-flex;
    align-items: center;
    gap: 0.32rem;
  }

  .legend-line {
    width: 0.85rem;
    height: 2px;
    background: var(--reading-accent);
  }

  .legend-line.listening {
    background: var(--listening-accent);
  }

  .chapter-list {
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .chapter-row {
    position: relative;
    display: grid;
    grid-template-columns: 2.4rem minmax(0, 1fr) auto;
    align-items: center;
    min-height: 3.75rem;
    padding: 0.55rem 0.65rem 0.55rem calc(0.7rem + var(--chapter-depth) * 0.85rem);
    border-bottom: 1px solid color-mix(in srgb, var(--color-border-subtle) 74%, transparent);
    transition: background var(--duration-normal) var(--ease-mechanical);
  }

  .chapter-row:last-child {
    border-bottom: 0;
  }

  .chapter-row:hover,
  .chapter-row:focus-within,
  .chapter-row.is-current {
    background: color-mix(in srgb, var(--reading-accent) 4%, var(--color-surface-2));
  }

  .progress-rails {
    position: absolute;
    inset: 0 auto 0 0;
    display: flex;
    gap: 2px;
    width: 6px;
  }

  .progress-rail {
    width: 2px;
    background: var(--reading-accent);
    box-shadow: 0 0 10px color-mix(in srgb, var(--reading-accent) 34%, transparent);
  }

  .progress-rail.listening {
    background: var(--listening-accent);
    box-shadow: 0 0 10px color-mix(in srgb, var(--listening-accent) 34%, transparent);
  }

  .chapter-number {
    color: var(--color-text-disabled);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.64rem;
    font-variant-numeric: tabular-nums;
  }

  .chapter-copy {
    min-width: 0;
  }

  .chapter-copy h3 {
    overflow: hidden;
    margin: 0;
    color: var(--color-text-primary);
    font-family: var(--font-body, "Inter", sans-serif);
    font-size: 0.84rem;
    font-weight: 520;
    line-height: 1.25;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .current-labels {
    display: flex;
    flex-wrap: wrap;
    gap: 0.28rem 0.65rem;
    margin-top: 0.2rem;
  }

  .current-label {
    color: color-mix(in srgb, var(--reading-accent) 72%, white 22%);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.57rem;
    letter-spacing: 0.04em;
  }

  .current-label.listening {
    color: color-mix(in srgb, var(--listening-accent) 72%, white 22%);
  }

  .chapter-actions {
    display: flex;
    align-items: center;
    gap: 0.12rem;
    padding-left: 0.5rem;
  }

  :global(.chapter-action) {
    border: 1px solid transparent;
    border-radius: var(--radius-xs);
  }

  :global(.chapter-action.read-action:hover),
  :global(.chapter-action.read-action:focus-visible) {
    border-color: color-mix(in srgb, var(--reading-accent) 46%, transparent);
    background: color-mix(in srgb, var(--reading-accent) 12%, transparent);
    color: color-mix(in srgb, var(--reading-accent) 76%, white 18%);
  }

  :global(.chapter-action.listen-action:hover),
  :global(.chapter-action.listen-action:focus-visible) {
    border-color: color-mix(in srgb, var(--listening-accent) 46%, transparent);
    background: color-mix(in srgb, var(--listening-accent) 12%, transparent);
    color: color-mix(in srgb, var(--listening-accent) 76%, white 18%);
  }

  :global(.chapter-action.combined-action) {
    color: color-mix(in srgb, var(--reading-accent) 52%, var(--listening-accent));
  }

  :global(.chapter-action.combined-action:hover),
  :global(.chapter-action.combined-action:focus-visible) {
    border-color: color-mix(in srgb, var(--reading-accent) 35%, var(--listening-accent));
    background: linear-gradient(
      135deg,
      color-mix(in srgb, var(--reading-accent) 13%, transparent),
      color-mix(in srgb, var(--listening-accent) 13%, transparent)
    );
    color: var(--color-text-primary);
  }

  @media (max-width: 520px) {
    .chapter-header {
      align-items: start;
      padding-inline: 0.75rem;
    }

    .chapter-legend {
      display: grid;
      justify-items: end;
      gap: 0.3rem;
    }

    .chapter-row {
      grid-template-columns: 1.8rem minmax(0, 1fr) auto;
      padding-right: 0.4rem;
    }

    .chapter-actions {
      gap: 0;
      padding-left: 0.2rem;
    }

    :global(.chapter-action) {
      width: 2rem;
      height: 2rem;
    }
  }
</style>
