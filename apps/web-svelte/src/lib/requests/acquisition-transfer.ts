// Maps qBittorrent torrent states to friendly download stages for the acquisition status view.
// prism-vocab: external — qBittorrent state strings, matched only here purely for display labels.
const STAGE_LABELS: Record<string, string> = {
  allocating: "Allocating",
  metaDL: "Fetching metadata",
  forcedMetaDL: "Fetching metadata",
  downloading: "Downloading",
  forcedDL: "Downloading",
  stalledDL: "Stalled — looking for peers",
  queuedDL: "Queued",
  checkingDL: "Verifying",
  checkingResumeData: "Verifying",
  moving: "Moving files",
  pausedDL: "Paused",
  uploading: "Seeding",
  forcedUP: "Seeding",
  stalledUP: "Seeding",
  queuedUP: "Seeding",
  checkingUP: "Verifying",
  pausedUP: "Complete",
  error: "Error",
  missingFiles: "Missing files",
};

/** Friendly label for a qBittorrent state; falls back to the raw state, or "Connecting…" when unknown. */
export function transferStageLabel(state: string | null | undefined): string {
  if (!state) return "Connecting…";
  return STAGE_LABELS[state] ?? state;
}

// States where nothing is actively progressing — no spinner/pulse indicator.
const SETTLED_STATES = new Set(["pausedDL", "pausedUP", "error", "missingFiles"]);

/** Whether the transfer is actively working (drives the activity indicator). */
export function isTransferActive(state: string | null | undefined): boolean {
  return !state || !SETTLED_STATES.has(state);
}
