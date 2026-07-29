<script lang="ts">
  import { BookOpen, Headphones, Layers2 } from "@lucide/svelte";
  import { Button, Panel } from "@prismedia/ui-svelte";

  interface Props {
    progressPercent: number;
    progressLabel?: string | null;
    completed?: boolean;
    activityLabel?: string | null;
    primaryColor?: string;
    secondaryColor?: string;
    onRead: () => void;
    onListen: () => void;
    onCombined: () => void;
  }

  let {
    progressPercent,
    progressLabel = null,
    completed = false,
    activityLabel = null,
    primaryColor = "var(--color-accent-400)",
    secondaryColor = "var(--color-accent-200)",
    onRead,
    onListen,
    onCombined,
  }: Props = $props();

  const percent = $derived(Math.max(0, Math.min(100, progressPercent)));
  const actionPrefix = $derived(completed ? "Start" : percent > 0 ? "Continue" : "Start");
</script>

<section
  class="combined-progress-section"
  aria-labelledby="combined-progress-title"
  style:--reading-accent={primaryColor}
  style:--listening-accent={secondaryColor}
>
  <Panel variant="panel" class="combined-progress-card">
    <div class="combined-copy">
      <p class="kicker">One position · two formats</p>
      <h2 id="combined-progress-title">Continue your book</h2>
      <p class="explanation">
        Reading and listening share this position. Either one moves it forward.
      </p>
    </div>

    <div class="progress-summary" aria-label="Book progress">
      <div class="progress-heading">
        <Layers2 class="h-4 w-4" />
        <span>{progressLabel ?? `${Math.round(percent)}%`}</span>
      </div>
      <div class="meter" aria-hidden="true">
        <span style:width={`${percent}%`}></span>
      </div>
      {#if activityLabel}
        <span class="activity-label">{activityLabel}</span>
      {/if}
    </div>

    <div class="combined-actions">
      <Button variant="secondary" size="sm" class="read-button gap-1.5" onclick={onRead}>
        <BookOpen class="h-3.5 w-3.5" />
        {actionPrefix} reading
      </Button>
      <Button variant="secondary" size="sm" class="listen-button gap-1.5" onclick={onListen}>
        <Headphones class="h-3.5 w-3.5" />
        {actionPrefix} listening
      </Button>
      <Button variant="primary" size="sm" class="combined-button gap-1.5" onclick={onCombined}>
        <Layers2 class="h-3.5 w-3.5" />
        {actionPrefix} both
      </Button>
    </div>
  </Panel>
</section>

<style>
  .combined-progress-section { min-width: 0; }

  :global(.combined-progress-card) {
    position: relative;
    display: grid;
    grid-template-columns: minmax(12rem, 0.85fr) minmax(14rem, 1fr) auto;
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
    background: linear-gradient(var(--reading-accent), var(--listening-accent));
    content: "";
  }

  .combined-copy, .progress-summary { min-width: 0; }
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
  .progress-summary { display: grid; gap: 0.55rem; }
  .progress-heading {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    color: color-mix(in srgb, var(--reading-accent) 72%, white 20%);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
  }
  .meter {
    height: 3px;
    overflow: hidden;
    background: color-mix(in srgb, var(--reading-accent) 12%, var(--color-border-subtle));
  }
  .meter span {
    display: block;
    height: 100%;
    background: linear-gradient(90deg, var(--reading-accent), var(--listening-accent));
  }
  .activity-label {
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
  }
  .combined-actions {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: 0.45rem;
  }
  :global(.read-button:hover), :global(.read-button:focus-visible) {
    border-color: color-mix(in srgb, var(--reading-accent) 45%, transparent);
  }
  :global(.listen-button:hover), :global(.listen-button:focus-visible) {
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
    :global(.combined-progress-card) { grid-template-columns: minmax(0, 1fr) minmax(14rem, 1fr); }
    .combined-actions { grid-column: 1 / -1; justify-content: flex-start; }
  }
  @media (max-width: 620px) {
    :global(.combined-progress-card) { grid-template-columns: 1fr; padding: 0.9rem 0.85rem 0.85rem 1rem; }
    .combined-actions { grid-column: auto; }
    :global(.combined-actions > button) { flex: 1 1 auto; }
  }
</style>
