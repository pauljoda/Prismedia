<script lang="ts">
  import { BookOpen, Headphones, Layers2 } from "@lucide/svelte";
  import { Button, Panel } from "@prismedia/ui-svelte";

  interface Props {
    readingPercent: number;
    listeningPercent: number;
    readingLabel?: string | null;
    listeningLabel?: string | null;
    readingCompleted?: boolean;
    listeningCompleted?: boolean;
    primaryColor?: string;
    secondaryColor?: string;
    onRead: () => void;
    onListen: () => void;
    onCombined: () => void;
  }

  let {
    readingPercent,
    listeningPercent,
    readingLabel = null,
    listeningLabel = null,
    readingCompleted = false,
    listeningCompleted = false,
    primaryColor = "var(--color-accent-400)",
    secondaryColor = "var(--color-accent-200)",
    onRead,
    onListen,
    onCombined,
  }: Props = $props();

  const readPercent = $derived(Math.max(0, Math.min(100, readingPercent)));
  const listenPercent = $derived(Math.max(0, Math.min(100, listeningPercent)));
  const readActionLabel = $derived(
    readingCompleted ? "Read again" : readPercent > 0 ? "Continue reading" : "Start reading",
  );
  const listenActionLabel = $derived(
    listeningCompleted ? "Listen again" : listenPercent > 0 ? "Continue listening" : "Start listening",
  );
  const combinedActionLabel = $derived(
    readingCompleted && listeningCompleted
      ? "Read and listen again"
      : readPercent > 0 || listenPercent > 0
        ? "Continue reading and listening"
        : "Start reading and listening",
  );
</script>

<section
  class="combined-progress-section"
  aria-labelledby="combined-progress-title"
  style:--reading-accent={primaryColor}
  style:--listening-accent={secondaryColor}
>
  <Panel variant="panel" class="combined-progress-card">
    <div class="combined-copy">
      <p class="kicker">Two ways through</p>
      <h2 id="combined-progress-title">Continue your book</h2>
      <p class="explanation">
        Pick up either edition, or align both from whichever is farther along.
      </p>
    </div>

    <div class="progress-pair" aria-label="Reading and listening progress">
      <div class="progress-row reading">
        <BookOpen class="h-4 w-4" />
        <span class="progress-name">Reading</span>
        <span class="progress-label">{readingLabel ?? `${Math.round(readPercent)}%`}</span>
        <div class="meter" aria-hidden="true">
          <span style:width={`${readPercent}%`}></span>
        </div>
      </div>
      <div class="progress-row listening">
        <Headphones class="h-4 w-4" />
        <span class="progress-name">Listening</span>
        <span class="progress-label">{listeningLabel ?? `${Math.round(listenPercent)}%`}</span>
        <div class="meter" aria-hidden="true">
          <span style:width={`${listenPercent}%`}></span>
        </div>
      </div>
    </div>

    <div class="combined-actions">
      <Button variant="secondary" size="sm" class="read-button gap-1.5" onclick={onRead}>
        <BookOpen class="h-3.5 w-3.5" />
        {readActionLabel}
      </Button>
      <Button variant="secondary" size="sm" class="listen-button gap-1.5" onclick={onListen}>
        <Headphones class="h-3.5 w-3.5" />
        {listenActionLabel}
      </Button>
      <Button variant="primary" size="sm" class="combined-button gap-1.5" onclick={onCombined}>
        <Layers2 class="h-3.5 w-3.5" />
        {combinedActionLabel}
      </Button>
    </div>
  </Panel>
</section>

<style>
  .combined-progress-section {
    min-width: 0;
  }

  :global(.combined-progress-card) {
    position: relative;
    display: grid;
    grid-template-columns: minmax(12rem, 0.85fr) minmax(16rem, 1.25fr) auto;
    gap: 1rem 1.4rem;
    align-items: center;
    overflow: hidden;
    padding: 1rem;
    border-color: color-mix(in srgb, var(--reading-accent) 18%, var(--color-border-default));
    background:
      linear-gradient(112deg, color-mix(in srgb, var(--reading-accent) 7%, transparent), transparent 38%),
      linear-gradient(292deg, color-mix(in srgb, var(--listening-accent) 6%, transparent), transparent 34%),
      var(--color-surface-panel, #0c0f15);
  }

  :global(.combined-progress-card)::before {
    position: absolute;
    inset: 0 auto 0 0;
    width: 4px;
    background: linear-gradient(var(--reading-accent) 0 50%, var(--listening-accent) 50% 100%);
    content: "";
  }

  .combined-copy {
    min-width: 0;
  }

  .kicker {
    margin: 0 0 0.2rem;
    color: var(--color-text-disabled);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.58rem;
    letter-spacing: 0.17em;
    text-transform: uppercase;
  }

  h2 {
    margin: 0;
    color: var(--color-text-primary);
    font-family: var(--font-heading, "Geist", sans-serif);
    font-size: 1rem;
    font-weight: 600;
  }

  .explanation {
    max-width: 30rem;
    margin: 0.35rem 0 0;
    color: var(--color-text-muted);
    font-size: 0.72rem;
    line-height: 1.45;
  }

  .progress-pair {
    display: grid;
    gap: 0.65rem;
    min-width: 0;
  }

  .progress-row {
    display: grid;
    grid-template-columns: 1rem 4.6rem minmax(5rem, auto);
    gap: 0.45rem;
    align-items: center;
    color: color-mix(in srgb, var(--reading-accent) 72%, white 20%);
  }

  .progress-row.listening {
    color: color-mix(in srgb, var(--listening-accent) 72%, white 20%);
  }

  .progress-name,
  .progress-label {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.64rem;
  }

  .progress-label {
    overflow: hidden;
    color: var(--color-text-secondary);
    text-align: right;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .meter {
    grid-column: 2 / -1;
    height: 2px;
    overflow: hidden;
    background: color-mix(in srgb, var(--reading-accent) 12%, var(--color-border-subtle));
  }

  .meter span {
    display: block;
    height: 100%;
    background: var(--reading-accent);
  }

  .listening .meter {
    background: color-mix(in srgb, var(--listening-accent) 12%, var(--color-border-subtle));
  }

  .listening .meter span {
    background: var(--listening-accent);
  }

  .combined-actions {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: 0.45rem;
  }

  :global(.read-button:hover),
  :global(.read-button:focus-visible) {
    border-color: color-mix(in srgb, var(--reading-accent) 45%, transparent);
  }

  :global(.listen-button:hover),
  :global(.listen-button:focus-visible) {
    border-color: color-mix(in srgb, var(--listening-accent) 45%, transparent);
  }

  :global(.combined-button) {
    border-color: color-mix(in srgb, var(--reading-accent) 30%, var(--listening-accent));
    background: linear-gradient(
      135deg,
      color-mix(in srgb, var(--reading-accent) 42%, #1b2029),
      color-mix(in srgb, var(--listening-accent) 38%, #1b2029)
    );
    color: var(--color-text-primary);
  }

  @media (max-width: 960px) {
    :global(.combined-progress-card) {
      grid-template-columns: minmax(0, 1fr) minmax(16rem, 1fr);
    }

    .combined-actions {
      grid-column: 1 / -1;
      justify-content: flex-start;
    }
  }

  @media (max-width: 620px) {
    :global(.combined-progress-card) {
      grid-template-columns: 1fr;
      padding: 0.9rem 0.85rem 0.85rem 1rem;
    }

    .combined-actions {
      grid-column: auto;
    }

    :global(.combined-actions > button) {
      flex: 1 1 auto;
    }
  }
</style>
