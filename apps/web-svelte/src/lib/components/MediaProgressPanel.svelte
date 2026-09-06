<script lang="ts">
  import { Button, Progress, ToggleButton } from "@prismedia/ui-svelte";
  import { Play, RotateCcw, Eye, Glasses, Headphones } from "@lucide/svelte";

  type ProgressKind = "watch" | "read" | "listen";

  interface Props {
    /** Whether this panel tracks watching, reading, or listening. Drives labels only. */
    kind: ProgressKind;
    /** Whether the item is marked watched/read. */
    completed: boolean;
    /** Progress through the item, 0..100. */
    percent: number;
    /** Human-readable position, e.g. "12:34 / 32:31" or "Page 45 of 200". */
    positionLabel?: string | null;
    /** Optional supplementary line, e.g. "Played 3 times". */
    countLabel?: string | null;
    /** Shows the Resume action. */
    canResume?: boolean;
    /** Shows the Start Over action. */
    canStartOver?: boolean;
    /** Disables actions while a mutation is in flight. */
    busy?: boolean;
    /** Toggles the watched/read state. Independent of position by design. */
    onToggleCompleted: (next: boolean) => void;
    /** Resumes from the saved position. */
    onResume?: () => void;
    /** Action copy for the saved target. Containers use Continue; time/page media use Resume. */
    resumeLabel?: string;
    /** Resets to the beginning. */
    onStartOver?: () => void;
  }

  let {
    kind,
    completed,
    percent,
    positionLabel = null,
    countLabel = null,
    canResume = false,
    canStartOver = false,
    busy = false,
    onToggleCompleted,
    onResume,
    resumeLabel = "Resume",
    onStartOver,
  }: Props = $props();


  const clampedPercent = $derived(Math.min(100, Math.max(0, percent)));
  const copy = $derived.by(() => {
    if (kind === "watch") {
      return { kicker: "Playback", active: "In progress", complete: "Watched", mark: "Mark watched", unmark: "Mark unwatched" };
    }
    if (kind === "listen") {
      return { kicker: "Listening", active: "Listening", complete: "Listened", mark: "Mark listened", unmark: "Mark unlistened" };
    }
    return { kicker: "Reading", active: "Reading", complete: "Read", mark: "Mark read", unmark: "Mark unread" };
  });
  const statusLabel = $derived(completed ? copy.complete : clampedPercent > 0 ? copy.active : "Not started");
  const toggleTitle = $derived(completed ? copy.unmark : copy.mark);
  const showMeter = $derived(!completed && clampedPercent > 0);

  function toggle() {
    onToggleCompleted(!completed);
  }
</script>

<section class="progress-panel">
  <div class="head">
    <span class="kicker">{copy.kicker}</span>
    <span class="status" class:complete={completed}>{statusLabel}</span>
  </div>

  {#if positionLabel || showMeter || countLabel}
    <div class="lines">
      {#if positionLabel}
        <span class="position">{positionLabel}</span>
      {/if}
      {#if showMeter}
        <span class="percent">{Math.round(clampedPercent)}%</span>
      {/if}
      {#if countLabel}
        <span class="count">{countLabel}</span>
      {/if}
    </div>
  {/if}

  {#if showMeter}
    <Progress value={clampedPercent} aria-label={`${copy.kicker} progress`} />
  {/if}

  <div class="footer">
    <div class="buttons">
      {#if canResume && onResume}
        <Button
          variant="primary" size="sm"
          onclick={onResume}
          disabled={busy}
        >
          <Play class="h-3.5 w-3.5" />
          <span class="entity-action-button-label">{resumeLabel}</span>
        </Button>
      {/if}
      {#if canStartOver && onStartOver}
        <Button variant="outline" size="sm" onclick={onStartOver} disabled={busy}>
          <RotateCcw class="h-3.5 w-3.5" />
          <span class="entity-action-button-label">Start over</span>
        </Button>
      {/if}
    </div>

    <ToggleButton
      variant="outline" size="sm"
      bind:pressed={() => completed, () => toggle()}
      title={toggleTitle}
      aria-label={toggleTitle}
      disabled={busy}
    >
      {#if kind === "listen"}
        <Headphones class="h-4 w-4" />
      {:else if kind === "watch"}
        <Eye class="h-4 w-4" />
      {:else}
        <Glasses class="h-4 w-4" />
      {/if}
    </ToggleButton>
  </div>
</section>

<style>
  .progress-panel {
    display: grid;
    gap: 0.65rem;
    padding: 0.85rem 1rem;
    border: 1px solid var(--color-border-default, rgba(164, 172, 185, 0.12));
    border-radius: var(--radius-md, 10px);
    background: var(--color-surface-2, #11161d);
  }

  .head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
  }

  .kicker {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--color-text-disabled, #5f687a);
  }

  .status {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    color: var(--color-text-muted, #8a93a6);
  }

  .status.complete {
    color: var(--color-text-accent, #c7c9cc);
  }

  .lines {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 0.35rem 0.9rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.82rem;
    color: var(--color-text-secondary, #c4c9d4);
  }

  .percent {
    color: var(--color-text-accent, #c7c9cc);
  }

  .count {
    color: var(--color-text-muted, #8a93a6);
  }

  .footer {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
  }

  .buttons {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
  }

</style>
