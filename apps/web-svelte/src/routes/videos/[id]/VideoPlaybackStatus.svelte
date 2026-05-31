<script lang="ts">
  import MediaProgressPanel from "$lib/components/MediaProgressPanel.svelte";
  import { formatVideoTimestamp } from "./video-page-state";

  interface Props {
    /** Number of completed plays recorded for the video. */
    playCount: number;
    /** Stored resume position in seconds. */
    resumeSeconds: number;
    /** Total runtime in seconds, when known. */
    durationSeconds: number;
    /** Completion timestamp, or null when the video is not marked watched. */
    completedAt: string | null;
    /** Live playback position in seconds while the inline player is active; advances the panel as you watch. */
    livePositionSeconds?: number;
    /** Disables actions while a mutation is in flight. */
    busy?: boolean;
    /** Resumes inline playback from the stored position. */
    onResume: () => void;
    /** Resets playback to the beginning. */
    onStartOver: () => void;
    /** Toggles the watched state (independent of position). */
    onToggleWatched: (watched: boolean) => void;
  }

  let {
    playCount,
    resumeSeconds,
    durationSeconds,
    completedAt,
    livePositionSeconds = 0,
    busy = false,
    onResume,
    onStartOver,
    onToggleWatched,
  }: Props = $props();

  const watched = $derived(Boolean(completedAt));
  const hasResume = $derived(!watched && resumeSeconds > 5 && durationSeconds > 0);
  // While the inline player is active, follow its live position so the panel advances in real time;
  // otherwise fall back to the persisted resume point.
  const position = $derived(!watched && livePositionSeconds > 0 ? livePositionSeconds : resumeSeconds);
  const percent = $derived(
    watched ? 100 : durationSeconds > 0 ? (position / durationSeconds) * 100 : 0,
  );
  const positionLabel = $derived(
    durationSeconds > 0 && (position > 5 || watched)
      ? `${formatVideoTimestamp(watched ? durationSeconds : position)} / ${formatVideoTimestamp(durationSeconds)}`
      : null,
  );
  const countLabel = $derived(
    playCount <= 0 ? null : playCount === 1 ? "Played once" : `Played ${playCount} times`,
  );
</script>

<MediaProgressPanel
  kind="watch"
  completed={watched}
  {percent}
  {positionLabel}
  {countLabel}
  canResume={hasResume}
  canStartOver={watched || resumeSeconds > 0}
  {busy}
  onToggleCompleted={onToggleWatched}
  {onResume}
  {onStartOver}
/>
