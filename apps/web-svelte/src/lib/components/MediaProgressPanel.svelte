<script lang="ts">
  import { Play, RotateCcw, Glasses, Headphones } from "@lucide/svelte";

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
    onStartOver,
  }: Props = $props();

  let animating = $state(false);

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
    animating = true;
    setTimeout(() => (animating = false), 350);
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
    <div class="meter-track" aria-hidden="true">
      <div class="meter-fill" style:width={`${clampedPercent}%`}></div>
    </div>
  {/if}

  <div class="footer">
    <div class="buttons">
      {#if canResume && onResume}
        <button
          type="button"
          class="entity-action-button entity-action-button-primary"
          onclick={onResume}
          disabled={busy}
        >
          <Play class="h-3.5 w-3.5" />
          <span class="entity-action-button-label">Resume</span>
        </button>
      {/if}
      {#if canStartOver && onStartOver}
        <button type="button" class="entity-action-button" onclick={onStartOver} disabled={busy}>
          <RotateCcw class="h-3.5 w-3.5" />
          <span class="entity-action-button-label">Start over</span>
        </button>
      {/if}
    </div>

    <button
      type="button"
      class="status-toggle"
      class:active={completed}
      class:animating
      title={toggleTitle}
      aria-label={toggleTitle}
      aria-pressed={completed}
      onclick={toggle}
      disabled={busy}
    >
      {#if kind === "listen"}
        <Headphones class="h-4 w-4" />
      {:else}
        <Glasses class="h-4 w-4" />
      {/if}
    </button>
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

  /* Mirrors the favorite/organized .action-badge toggles on entity detail pages. */
  .status-toggle {
    display: grid;
    place-items: center;
    flex: none;
    width: 1.75rem;
    height: 1.75rem;
    padding: 0;
    border: 1px solid var(--color-border, #1c2235);
    border-radius: var(--radius-xs, 4px);
    background: rgba(255, 255, 255, 0.04);
    color: var(--color-text-disabled, #4a5260);
    cursor: pointer;
    transition: color 0.2s, border-color 0.2s, box-shadow 0.2s, transform 0.2s;
  }

  .status-toggle:hover:not(:disabled) {
    color: var(--color-text-secondary, #c4c9d4);
    border-color: var(--color-border-accent-strong, rgba(199, 201, 204, 0.52));
  }

  .status-toggle.active {
    color: var(--color-text-accent, #c7c9cc);
    border-color: rgba(199, 201, 204, 0.5);
    box-shadow: 0 0 10px rgba(199, 201, 204, 0.2);
  }

  .status-toggle.animating {
    animation: badge-pop 0.35s cubic-bezier(0.175, 0.885, 0.32, 1.275);
  }

  .status-toggle:disabled {
    opacity: 0.55;
    cursor: default;
  }

  @keyframes badge-pop {
    0% {
      transform: scale(1);
    }
    40% {
      transform: scale(1.3);
    }
    100% {
      transform: scale(1);
    }
  }
</style>
